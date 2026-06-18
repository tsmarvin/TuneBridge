using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Worker.CacheBootstrap;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="StaleCacheRefreshBackgroundService"/>: cadence (startup grace, durable
/// schedule), the bootstrap-running skip guard, bounded age-only selection, leg decomposition,
/// multi-leg seeding, per-queue routing, the mode-B registration-set pin, saga keying, bulk-priority
/// enforcement, fire-and-forget behavior, per-record resilience, cancellation, and config binding.
/// </summary>
[TestClass]
public class StaleCacheRefreshBackgroundServiceTests {

    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    private Mock<IProviderQueueResolver<QueuedLookupRequest>> _queueResolverMock = null!;
    private Mock<IRequestQueue<QueuedLookupRequest>> _queueMock = null!;
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _redisDatabaseMock = null!;
    private Mock<ILogger<StaleCacheRefreshBackgroundService>> _loggerMock = null!;
    private HashSet<SupportedProviders> _enabledProviders = null!;
    private CacheBootstrapSettings _settings = null!;

    /// <summary>MSTest-injected context, used to flow the test's cancellation token into awaits.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    private const string TestUserDid = "did:plc:testuser123";

    /// <summary>Creates fresh mocks and default settings before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _sagaManagerMock = new Mock<ISagaStateManager>( );
        _queueResolverMock = new Mock<IProviderQueueResolver<QueuedLookupRequest>>( );
        _queueMock = new Mock<IRequestQueue<QueuedLookupRequest>>( );
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _redisDatabaseMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<StaleCacheRefreshBackgroundService>>( );

        _ = _loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        _ = _redisMock
            .Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _redisDatabaseMock.Object );
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( RedisValue.Null );
        _ = _redisDatabaseMock
            .Setup( d => d.StringSetAsync(
                It.IsAny<RedisKey>( ), It.IsAny<RedisValue>( ),
                It.IsAny<Expiration>( ), It.IsAny<ValueCondition>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( true );

        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( _queueMock.Object );
        _ = _queueMock
            .Setup( q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( MakeMinimalSaga( ) );
        _ = _sagaManagerMock
            .Setup( m => m.InitializeProviderStatesAsync(
                It.IsAny<string>( ),
                It.IsAny<IEnumerable<SupportedProviders>>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );
        _ = _sagaManagerMock
            .Setup( m => m.UpdateProviderStateAsync(
                It.IsAny<string>( ),
                It.IsAny<ProviderLookupState>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        _enabledProviders = [SupportedProviders.Spotify];

        _settings = MakeSettings( );
    }

    #region Cadence / Schedule Tests

    /// <summary>
    /// At service startup <c>ListAllRecordsAsync</c> must NOT be called before the 120-second startup
    /// grace period elapses. A 200 ms observation window is used (far shorter than the 120 s grace),
    /// so the pass cannot fire.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_AtStartup_DoesNotCallListAllRecordsBeforeGrace( ) {
        // The interval is very short so the schedule would fire immediately after the grace,
        // but we cancel well before the 120 s grace ends.
        _settings = MakeSettings( refreshInterval: TimeSpan.FromMilliseconds( 1 ) );
        StaleCacheRefreshBackgroundService service = CreateService( );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );

        Task executeTask = service.StartAsync( cts.Token );

        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// The durable marker (<c>cache:refresh:last-run</c>) is written via <c>StringSetAsync</c>
    /// BEFORE <c>ListAllRecordsAsync</c> is called. A restart mid-drip therefore sees a fresh
    /// marker and waits the interval remainder rather than re-selecting.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WritesMarkerBeforeSelection( ) {
        int markerWriteOrder = -1;
        int listCallOrder = -1;
        int callCounter = 0;

        // Signals when ListAllRecordsAsync has been called so we know both events have fired
        // and the ordering assertion is safe to read.
        TaskCompletionSource listCalled = new( TaskCreationOptions.RunContinuationsAsynchronously );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );

        _ = _redisDatabaseMock
            .Setup( d => d.StringSetAsync(
                "cache:refresh:last-run", It.IsAny<RedisValue>( ),
                It.IsAny<Expiration>( ), It.IsAny<ValueCondition>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags>( ( _, _, _, _, _ ) => {
                markerWriteOrder = System.Threading.Interlocked.Increment( ref callCounter );
            } )
            .ReturnsAsync( true );

        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns<Uri, string, CancellationToken>( ( _, _, _ ) => {
                listCallOrder = System.Threading.Interlocked.Increment( ref callCounter );
                _ = listCalled.TrySetResult( );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        StaleCacheRefreshBackgroundService service = CreateService( );
        service._startupGrace = TimeSpan.Zero;
        service._maxJitter = TimeSpan.Zero;

        _ = service.StartAsync( cts.Token );

        // Wait until the selection phase fires, then cancel so the loop terminates.
        await listCalled.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        Assert.AreNotEqual( -1, markerWriteOrder, "Marker write must occur" );
        Assert.AreNotEqual( -1, listCallOrder, "ListAllRecordsAsync must be called" );
        Assert.IsLessThan( listCallOrder, markerWriteOrder,
            "Marker must be written before selection begins" );
    }

    /// <summary>
    /// When the marker is absent, the service treats elapsed time as infinite (due-now): driving
    /// <c>ExecuteAsync</c> via <c>StartAsync</c> with zero grace and jitter and a 6-hour interval,
    /// <c>ListAllRecordsAsync</c> fires within 5 seconds because no schedule wait is imposed.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Schedule_NoMarker_TreatsAsDueNow( ) {
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( "cache:refresh:last-run", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( RedisValue.Null );

        TaskCompletionSource listCalled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns<Uri, string, CancellationToken>( ( _, _, _ ) => {
                _ = listCalled.TrySetResult( );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        _settings = MakeSettings( refreshInterval: TimeSpan.FromHours( 6 ) );
        StaleCacheRefreshBackgroundService service = CreateService( );
        service._startupGrace = TimeSpan.Zero;
        service._maxJitter = TimeSpan.Zero;

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );
        _ = service.StartAsync( cts.Token );

        await listCalled.Task.WaitAsync( TimeSpan.FromSeconds( 5 ), TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.AtLeastOnce
        );
    }

    /// <summary>
    /// When the marker read throws, the service treats it as due-now (fail-toward-running) and
    /// proceeds: driving <c>ExecuteAsync</c> via <c>StartAsync</c> with zero grace and jitter and
    /// a 6-hour interval, <c>ListAllRecordsAsync</c> fires within 5 seconds despite the exception.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Schedule_MarkerReadThrows_FailsTowardRunning( ) {
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( "cache:refresh:last-run", It.IsAny<CommandFlags>( ) ) )
            .ThrowsAsync( new RedisConnectionException( ConnectionFailureType.UnableToConnect, "test" ) );

        TaskCompletionSource listCalled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns<Uri, string, CancellationToken>( ( _, _, _ ) => {
                _ = listCalled.TrySetResult( );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        _settings = MakeSettings( refreshInterval: TimeSpan.FromHours( 6 ) );
        StaleCacheRefreshBackgroundService service = CreateService( );
        service._startupGrace = TimeSpan.Zero;
        service._maxJitter = TimeSpan.Zero;

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );
        _ = service.StartAsync( cts.Token );

        await listCalled.Task.WaitAsync( TimeSpan.FromSeconds( 5 ), TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.AtLeastOnce
        );
    }

    /// <summary>
    /// A future-dated marker (e.g. clock skew) must be treated as due-now, not cause an
    /// over-long wait. Driving <c>ExecuteAsync</c> with zero grace and jitter and a 6-hour
    /// interval, a marker set one hour in the future fires the pass promptly rather than stalling
    /// for approximately 7 hours.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Schedule_FutureDatedMarker_TreatsAsDueNow( ) {
        string futureMarker = DateTimeOffset.UtcNow.AddHours( 1 ).ToString( "O" );
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( "cache:refresh:last-run", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)futureMarker );

        TaskCompletionSource listCalled = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns<Uri, string, CancellationToken>( ( _, _, _ ) => {
                _ = listCalled.TrySetResult( );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        _settings = MakeSettings( refreshInterval: TimeSpan.FromHours( 6 ) );
        StaleCacheRefreshBackgroundService service = CreateService( );
        service._startupGrace = TimeSpan.Zero;
        service._maxJitter = TimeSpan.Zero;

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );
        _ = service.StartAsync( cts.Token );

        await listCalled.Task.WaitAsync( TimeSpan.FromSeconds( 5 ), TestContext.CancellationToken );
        await cts.CancelAsync( );
        await service.StopAsync( CancellationToken.None );

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.AtLeastOnce
        );
    }

    #endregion

    #region Skip Guard Tests

    /// <summary>
    /// When the bootstrap status document reports <c>IsRunning = true</c>, the refresh pass must
    /// skip selection and enqueue no records.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WhenBootstrapIsRunning_SkipsSelectionAndEnqueue( ) {
        string runningJson = System.Text.Json.JsonSerializer.Serialize(
            new CacheBootstrapStatus { IsRunning = true } );
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( CacheBootstrapStatus.RedisKey, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)runningJson );

        StaleCacheRefreshBackgroundService service = CreateService( );

        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// When the bootstrap status document reports <c>IsRunning = false</c>, the refresh pass proceeds.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WhenBootstrapIsNotRunning_ProceedsWithSelection( ) {
        string notRunningJson = System.Text.Json.JsonSerializer.Serialize(
            new CacheBootstrapStatus { IsRunning = false } );
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( CacheBootstrapStatus.RedisKey, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)notRunningJson );

        SetupRecordList( [MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ) )] );
        StaleCacheRefreshBackgroundService service = CreateService( );

        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// When the bootstrap status is absent from Redis, the pass treats it as not-running and proceeds.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WhenStatusAbsent_ProceedsWithSelection( ) {
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( RedisValue.Null );

        SetupRecordList( [MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ) )] );
        StaleCacheRefreshBackgroundService service = CreateService( );

        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    #endregion

    #region Selection Tests (Age-Only)

    /// <summary>
    /// When more than N stale records exist, exactly N are enqueued AND they are the N oldest by
    /// <c>LookedUpAt</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_MoreThanN_EnqueuesExactlyNOldest( ) {
        _settings = MakeSettings( maxRecordsPerRun: 2 );

        DateTime oldest = DateTime.UtcNow.AddDays( -90 );
        DateTime middle = DateTime.UtcNow.AddDays( -60 );
        DateTime newest = DateTime.UtcNow.AddDays( -31 );

        (string, MediaLinkResult) rec1 = MakeStaleRecord( "at://oldest", oldest, isrc: "ISRC_OLDEST" );
        (string, MediaLinkResult) rec2 = MakeStaleRecord( "at://middle", middle, isrc: "ISRC_MIDDLE" );
        (string, MediaLinkResult) rec3 = MakeStaleRecord( "at://newest", newest, isrc: "ISRC_NEWEST" );

        SetupRecordList( [rec2, rec3, rec1] );

        // Capture the saga lookup key from each GetOrCreateAsync call to determine which records
        // were selected. All three records share the same Spotify URL (same native leg LookupValue),
        // so the saga lookup key — derived from the ISRC — is the only distinguishing signal.
        List<string> sagaLookupKeys = [];
        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( _, lookupKey, _, _, _, _ ) => sagaLookupKeys.Add( lookupKey ) )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, sagaLookupKeys, "Expected exactly N=2 records selected" );
        Assert.Contains( k => k.Contains( "ISRC_OLDEST" ), sagaLookupKeys,
            "The oldest record must be among the selected." );
        Assert.Contains( k => k.Contains( "ISRC_MIDDLE" ), sagaLookupKeys,
            "The middle record must be among the selected." );
        Assert.DoesNotContain( k => k.Contains( "ISRC_NEWEST" ), sagaLookupKeys,
            "The newest record must NOT be among the selected." );
    }

    /// <summary>
    /// When fewer than N stale records exist, all stale records are enqueued.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_FewerThanN_EnqueuesAllStale( ) {
        _settings = MakeSettings( maxRecordsPerRun: 10 );

        SetupRecordList( [
            MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ) ),
            MakeStaleRecord( "at://test/2", DateTime.UtcNow.AddDays( -50 ) )
        ] );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    /// <summary>
    /// A record whose <c>LookedUpAt</c> is one second older than the cache window IS selected as
    /// stale; a record one day inside the window is NOT selected.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_BoundaryTimestamps_SelectsOlderNotNewer( ) {
        int cacheDays = 30;
        _settings = MakeSettings( cacheDays: cacheDays );

        DateTime justStale = DateTime.UtcNow.AddDays( -cacheDays ).AddSeconds( -1 );
        DateTime justFresh = DateTime.UtcNow.AddDays( -cacheDays ).AddDays( 1 );

        (string, MediaLinkResult) staleRec = MakeStaleRecord( "at://stale", justStale, isrc: "STALE_ISRC", forceIsPartial: false );
        (string, MediaLinkResult) freshRec = MakeFreshRecord( "at://fresh", justFresh, isrc: "FRESH_ISRC" );

        SetupRecordList( [staleRec, freshRec] );

        List<QueuedLookupRequest> enqueuedRequests = [];
        _ = _queueMock
            .Setup( q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>(
                ( req, _, _ ) => enqueuedRequests.Add( req ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 1, enqueuedRequests );
        // The stale record's Spotify URL parses to native track ID "3SPOTID12345"; the fresh record
        // is not selected, so a count of 1 with this native LookupValue confirms the correct record.
        Assert.AreEqual( LookupRequestType.SongIdLookup, enqueuedRequests[0].LookupType,
            "Stale record with parseable URL yields a native SongIdLookup leg." );
        Assert.AreEqual( "3SPOTID12345", enqueuedRequests[0].LookupValue,
            "Native leg LookupValue is the extracted track ID, not the ISRC." );
    }

    /// <summary>
    /// A record with <c>IsPartial = true</c> and a FRESH timestamp is NOT selected. This is the
    /// age-only behavior change: partial-ness alone is no longer a staleness trigger.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_PartialButFreshRecord_IsNotSelected( ) {
        DateTime fresh = DateTime.UtcNow.AddDays( -1 ); // well inside cache window

        // IsPartial=true, fresh timestamp — under age-only rule this must NOT be selected.
        (string, MediaLinkResult) partialFreshRec = MakeStaleRecord( "at://partial-fresh", fresh, isrc: "PARTIAL_ISRC", forceIsPartial: true );
        // IsPartial=false, fresh timestamp — also NOT selected.
        (string, MediaLinkResult) freshRec = MakeFreshRecord( "at://fresh", fresh, isrc: "FRESH_ISRC" );

        SetupRecordList( [partialFreshRec, freshRec] );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        // Neither record is stale under age-only freshness: zero enqueues expected.
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// A record with <c>IsPartial = true</c> and an EXPIRED timestamp IS selected (via the age arm).
    /// Confirms partial records do eventually get refreshed — just once per freshness window.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_PartialAndExpired_IsSelected( ) {
        DateTime expired = DateTime.UtcNow.AddDays( -31 ); // outside 30-day window

        (string, MediaLinkResult) partialExpiredRec = MakeStaleRecord(
            "at://partial-expired", expired, isrc: "PARTIAL_EXPIRED_ISRC", forceIsPartial: true );

        SetupRecordList( [partialExpiredRec] );

        List<QueuedLookupRequest> enqueuedRequests = [];
        _ = _queueMock
            .Setup( q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>(
                ( req, _, _ ) => enqueuedRequests.Add( req ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 1, enqueuedRequests );
        Assert.AreEqual( LookupRequestType.SongIdLookup, enqueuedRequests[0].LookupType,
            "Partial-expired record with parseable URL yields a native SongIdLookup leg." );
        Assert.AreEqual( "3SPOTID12345", enqueuedRequests[0].LookupValue,
            "Native leg LookupValue is the extracted track ID, not the ISRC." );
    }

    /// <summary>
    /// Equivalence pin: the age-only selection produces the same result set as the inline age check
    /// without the old <c>IsPartial</c> arm. A corpus with a partial+fresh record confirms it is
    /// excluded.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_SelectionEquivalentToAgeOnlyCheck_ExcludesPartialFresh( ) {
        int cacheDays = 30;
        _settings = MakeSettings( cacheDays: cacheDays );
        DateTime utcNow = DateTime.UtcNow;

        // Records: one expired non-partial, one fresh non-partial, one fresh partial.
        (string, MediaLinkResult) expiredRec = MakeStaleRecord(
            "at://expired", utcNow.AddDays( -60 ), isrc: "EXPIRED_ISRC", forceIsPartial: false );
        (string, MediaLinkResult) freshRec = MakeFreshRecord(
            "at://fresh", utcNow.AddDays( -1 ), isrc: "FRESH_ISRC" );
        (string, MediaLinkResult) partialFreshRec = MakeStaleRecord(
            "at://partial-fresh", utcNow.AddDays( -1 ), isrc: "PARTIAL_ISRC", forceIsPartial: true );

        SetupRecordList( [expiredRec, freshRec, partialFreshRec] );

        List<QueuedLookupRequest> enqueuedRequests = [];
        _ = _queueMock
            .Setup( q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>(
                ( req, _, _ ) => enqueuedRequests.Add( req ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        // Only the age-expired record should be selected.
        Assert.HasCount( 1, enqueuedRequests, "Only the expired record should be enqueued" );
        Assert.AreEqual( LookupRequestType.SongIdLookup, enqueuedRequests[0].LookupType,
            "Expired record with parseable URL yields a native SongIdLookup leg." );
        Assert.AreEqual( "3SPOTID12345", enqueuedRequests[0].LookupValue,
            "Native leg LookupValue is the extracted track ID, not the ISRC." );
    }

    #endregion

    #region Leg Decomposition Tests (DeriveRefreshLegs)

    /// <summary>
    /// All three providers present with parseable track URLs yields three native-id SongIdLookup
    /// legs and zero fallback legs.
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_AllProvidersPresentParseable_ThreeNativeLegsZeroFallback( ) {
        MediaLinkResult record = MakeThreeProviderRecord( isrc: "TESTISRC" );
        HashSet<SupportedProviders> providers = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, providers );

        Assert.HasCount( 3, legs, "Expected 3 native-id legs" );
        Assert.IsTrue( legs.All( l => l.LookupType is LookupRequestType.SongIdLookup ),
            "All legs should be SongIdLookup" );
        Assert.DoesNotContain( l => l.LookupType is LookupRequestType.IsrcLookup, legs,
            "No fallback legs expected" );
        // Each provider covered exactly once.
        CollectionAssert.AreEquivalent(
            providers.ToList( ),
            legs.Select( l => l.Provider ).ToList( )
        );
    }

    /// <summary>
    /// Album entries (IsAlbum=true) yield <c>AlbumIdLookup</c> legs; the per-entry IsAlbum is used.
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_AlbumEntries_YieldAlbumIdLookup( ) {
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/album/3TESTID111",
            IsAlbum = true,
            ExternalId = "UPCTESTVAL"
        };

        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, [SupportedProviders.Spotify] );

        Assert.HasCount( 1, legs );
        Assert.AreEqual( LookupRequestType.AlbumIdLookup, legs[0].LookupType );
        Assert.IsTrue( legs[0].IsAlbum );
    }

    /// <summary>
    /// Spotify+Apple present and parseable, Tidal absent, ISRC present → 2 native legs + 1 Tidal
    /// IsrcLookup fallback.
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_MissingProviderWithIsrc_EmitsExternalIdFallbackLeg( ) {
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = "testisrc",
            IsAlbum = false
        };
        record.Results[SupportedProviders.AppleMusic] = new MusicLookupResult {
            URL = "https://music.apple.com/us/album/something/1234567890?i=9876543210",
            ExternalId = "testisrc",
            IsAlbum = false
        };
        // Tidal absent from Results.

        HashSet<SupportedProviders> providers = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];
        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, providers );

        Assert.HasCount( 3, legs );

        StaleCacheRefreshBackgroundService.RefreshLeg tidalFallback =
            legs.Single( l => l.Provider == SupportedProviders.Tidal );
        Assert.AreEqual( LookupRequestType.IsrcLookup, tidalFallback.LookupType );
        Assert.AreEqual( "TESTISRC", tidalFallback.LookupValue,
            "ISRC fallback value should be uppercased" );
    }

    /// <summary>
    /// One provider absent, IsAlbum=true, UPC present → UpcLookup fallback leg; UPC is trim-only
    /// (not uppercased).
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_MissingProviderAlbumWithUpc_EmitsUpcFallbackLeg( ) {
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/album/3TESTID111",
            ExternalId = "  upc12345  ",
            IsAlbum = true
        };
        // Tidal absent.

        HashSet<SupportedProviders> providers = [SupportedProviders.Spotify, SupportedProviders.Tidal];
        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, providers );

        StaleCacheRefreshBackgroundService.RefreshLeg tidalFallback =
            legs.Single( l => l.Provider == SupportedProviders.Tidal );
        Assert.AreEqual( LookupRequestType.UpcLookup, tidalFallback.LookupType );
        Assert.AreEqual( "upc12345", tidalFallback.LookupValue,
            "UPC fallback value should be trimmed but NOT uppercased" );
    }

    /// <summary>
    /// Spotify present but URL is garbage (ExtractId returns null): treated as missing, gets an
    /// IsrcLookup fallback leg.
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_PresentButUnparseableUrl_TreatedAsMissing_GetsFallback( ) {
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://not-a-real-spotify-url.example/garbage",
            ExternalId = "GOODISRC",
            IsAlbum = false
        };

        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, [SupportedProviders.Spotify] );

        // No native leg (unparseable URL), one fallback.
        Assert.HasCount( 1, legs );
        Assert.AreEqual( LookupRequestType.IsrcLookup, legs[0].LookupType );
        Assert.AreEqual( SupportedProviders.Spotify, legs[0].Provider );
    }

    /// <summary>
    /// Tidal absent with no external id anywhere: no fallback leg emitted (nothing to search by).
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_MissingProviderNoExternalId_EmitsNoFallbackForIt( ) {
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = string.Empty,
            IsAlbum = false
        };
        // Tidal absent, no external id anywhere.

        HashSet<SupportedProviders> providers = [SupportedProviders.Spotify, SupportedProviders.Tidal];
        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, providers );

        // Only the Spotify native leg; no Tidal leg.
        Assert.HasCount( 1, legs );
        Assert.AreEqual( SupportedProviders.Spotify, legs[0].Provider );
        Assert.DoesNotContain( l => l.Provider == SupportedProviders.Tidal, legs );
    }

    /// <summary>
    /// All present providers unparseable and no external id → empty list (the skip case).
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_NoParseableUrlAndNoExternalId_ReturnsEmpty( ) {
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://garbage.example/noid",
            ExternalId = string.Empty
        };

        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, [SupportedProviders.Spotify] );

        Assert.HasCount( 0, legs );
    }

    /// <summary>
    /// A provider present in record.Results that is NOT in the enabled set produces no leg — neither
    /// a native-id leg nor a fallback leg. The provider drops off the record on the next refresh write.
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_DisabledProviderInResults_ProducesNoLeg( ) {
        // Record has both Spotify and Tidal with parseable URLs and an ISRC.
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = "TESTISRC",
            IsAlbum = false
        };
        record.Results[SupportedProviders.Tidal] = new MusicLookupResult {
            URL = "https://tidal.com/browse/track/12345678",
            ExternalId = "TESTISRC",
            IsAlbum = false
        };

        // Only Spotify is enabled; Tidal has been switched off.
        HashSet<SupportedProviders> onlySpotify = [SupportedProviders.Spotify];

        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, onlySpotify );

        // Only one leg (Spotify native); Tidal must not appear at all.
        Assert.HasCount( 1, legs );
        Assert.AreEqual( SupportedProviders.Spotify, legs[0].Provider );
        Assert.DoesNotContain( l => l.Provider == SupportedProviders.Tidal, legs,
            "A disabled provider must produce no leg, even when present in record.Results." );
    }

    /// <summary>
    /// At most one leg per provider: a provider gets a native leg XOR a fallback leg, never both.
    /// </summary>
    [TestMethod]
    public void DeriveRefreshLegs_AtMostOneLegPerProvider( ) {
        MediaLinkResult record = MakeThreeProviderRecord( isrc: "TESTISRC" );
        HashSet<SupportedProviders> providers = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        IReadOnlyList<StaleCacheRefreshBackgroundService.RefreshLeg> legs =
            StaleCacheRefreshBackgroundService.DeriveRefreshLegs( record, providers );

        IEnumerable<SupportedProviders> providerList = legs.Select( l => l.Provider );
        CollectionAssert.AllItemsAreUnique( providerList.ToList( ),
            "Each provider must appear at most once in the leg list" );
    }

    /// <summary>
    /// A record with no identifier (no parseable URL and no external id) is skipped (zero legs)
    /// and does not cause an error.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_RecordWithNoIdentifier_IsSkippedAndNotAnError( ) {
        MediaLinkResult result = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ), IsPartial = false };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = string.Empty,
            ExternalId = string.Empty,
            Title = string.Empty,
            Artist = string.Empty
        };

        SetupRecordList( [("at://noid", result)] );
        StaleCacheRefreshBackgroundService service = CreateService( );

        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Never
        );
    }

    #endregion

    #region Multi-Leg Seeding Tests (EnqueueRecordAsync / RunRefreshPassAsync)

    /// <summary>
    /// All legs for a single record share the same saga id; GetOrCreateAsync is called exactly once
    /// per record.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_MultiProviderRecord_AllLegsShareOneSagaId( ) {
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        SetupRecordList( [("at://three-provider", MakeThreeProviderRecord( isrc: "SHARED_ISRC" ))] );

        List<QueuedLookupRequest> captured = [];
        _ = _queueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>( ( r, _, _ ) => captured.Add( r ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 3, captured, "Expected 3 legs for a 3-provider record" );
        string firstSagaId = captured[0].SagaId;
        Assert.IsTrue( captured.All( r => r.SagaId == firstSagaId ),
            "All legs must share the same SagaId" );

        _sagaManagerMock.Verify(
            m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), It.IsAny<QueuePriority?>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "GetOrCreateAsync must be called exactly once per record" );
    }

    /// <summary>
    /// Each leg is enqueued to its own provider queue: GetQueue is called with the leg's provider
    /// and the request's Provider matches.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_EachLegEnqueuedToItsOwnProviderQueue( ) {
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        SetupRecordList( [("at://three-provider", MakeThreeProviderRecord( isrc: "TESTISRC" ))] );

        List<(SupportedProviders QueueProvider, SupportedProviders RequestProvider)> routingCapture = [];

        Mock<IRequestQueue<QueuedLookupRequest>> spotifyQ = new( );
        Mock<IRequestQueue<QueuedLookupRequest>> appleQ = new( );
        Mock<IRequestQueue<QueuedLookupRequest>> tidalQ = new( );

        foreach (Mock<IRequestQueue<QueuedLookupRequest>> q in new[] { spotifyQ, appleQ, tidalQ }) {
            _ = q.Setup( x => x.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
                .Returns( Task.CompletedTask );
        }

        _ = _queueResolverMock
            .Setup( r => r.GetQueue( SupportedProviders.Spotify ) ).Returns( spotifyQ.Object );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( SupportedProviders.AppleMusic ) ).Returns( appleQ.Object );
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( SupportedProviders.Tidal ) ).Returns( tidalQ.Object );

        List<QueuedLookupRequest> spotifyReqs = [];
        List<QueuedLookupRequest> appleReqs = [];
        List<QueuedLookupRequest> tidalReqs = [];

        _ = spotifyQ.Setup( x => x.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>( ( r, _, _ ) => spotifyReqs.Add( r ) )
            .Returns( Task.CompletedTask );
        _ = appleQ.Setup( x => x.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>( ( r, _, _ ) => appleReqs.Add( r ) )
            .Returns( Task.CompletedTask );
        _ = tidalQ.Setup( x => x.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>( ( r, _, _ ) => tidalReqs.Add( r ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 1, spotifyReqs );
        Assert.AreEqual( SupportedProviders.Spotify, spotifyReqs[0].Provider );

        Assert.HasCount( 1, appleReqs );
        Assert.AreEqual( SupportedProviders.AppleMusic, appleReqs[0].Provider );

        Assert.HasCount( 1, tidalReqs );
        Assert.AreEqual( SupportedProviders.Tidal, tidalReqs[0].Provider );
    }

    /// <summary>
    /// Every enqueued request carries <c>OriginPriority = QueuePriority.Bulk</c> AND the
    /// <c>EnqueueAsync</c> priority argument is also <c>QueuePriority.Bulk</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_AllLegs_CarryBulkOriginAndEnqueuePriority( ) {
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];
        SetupRecordList( [("at://three", MakeThreeProviderRecord( isrc: "TESTISRC" ))] );

        List<(QueuedLookupRequest Req, QueuePriority Priority)> captured = [];
        _ = _queueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>( ( r, p, _ ) => captured.Add( (r, p) ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsNotEmpty( captured, "At least one leg should be enqueued" );
        Assert.IsTrue( captured.All( c => c.Req.OriginPriority == QueuePriority.Bulk ),
            "All requests must have Bulk OriginPriority" );
        Assert.IsTrue( captured.All( c => c.Priority == QueuePriority.Bulk ),
            "All EnqueueAsync calls must use Bulk priority" );
    }

    /// <summary>
    /// <c>EnqueueAsync</c> must never be called with <c>QueuePriority.Interactive</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_NeverEnqueuesWithInteractivePriority( ) {
        SetupRecordList( [MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ) )] );
        StaleCacheRefreshBackgroundService service = CreateService( );

        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _queueMock.Verify(
            q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                QueuePriority.Interactive,
                It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    #endregion

    #region Mode-B Pin — Registration Set

    /// <summary>
    /// THE MODE-B PIN: InitializeProviderStatesAsync receives the exact enqueued-leg provider set,
    /// NOT all enabledProviders. For a record where only Spotify+Apple are present/parseable and there
    /// is no external id, Tidal must NOT appear in the registered set.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_InitializesProviderStatesForEnqueuedLegsOnly_NotAllEnabledProviders( ) {
        // Record: only Spotify+Apple parseable, no external id — Tidal gets no leg.
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = string.Empty,
            IsAlbum = false
        };
        record.Results[SupportedProviders.AppleMusic] = new MusicLookupResult {
            URL = "https://music.apple.com/us/album/x/1234567890?i=9876543210",
            ExternalId = string.Empty,
            IsAlbum = false
        };

        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        SetupRecordList( [("at://two-provider-no-external-id", record)] );

        IEnumerable<SupportedProviders>? capturedProviders = null;
        _ = _sagaManagerMock
            .Setup( m => m.InitializeProviderStatesAsync(
                It.IsAny<string>( ),
                It.IsAny<IEnumerable<SupportedProviders>>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<string, IEnumerable<SupportedProviders>, CancellationToken>(
                ( _, providers, _ ) => capturedProviders = [.. providers] )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsNotNull( capturedProviders, "InitializeProviderStatesAsync must be called" );
        HashSet<SupportedProviders> registered = [.. capturedProviders];

        // Positive: exactly {Spotify, Apple} registered.
        Assert.Contains( SupportedProviders.Spotify, registered );
        Assert.Contains( SupportedProviders.AppleMusic, registered );

        // Negative control: Tidal must NOT be registered (it has no leg).
        Assert.DoesNotContain( SupportedProviders.Tidal, registered,
            "Today's bug: registering all enabledProviders (including Tidal with no leg) keeps the saga incomplete forever. This assertion is the mode-B pin." );
    }

    /// <summary>
    /// For a 2-native + 1-fallback record, the registered set equals all three providers.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_RegisteredSet_EqualsDistinctLegProviders( ) {
        // Spotify+Apple parseable, Tidal absent but ISRC present → Tidal gets a fallback leg.
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = "TESTISRC",
            IsAlbum = false
        };
        record.Results[SupportedProviders.AppleMusic] = new MusicLookupResult {
            URL = "https://music.apple.com/us/album/x/1234567890?i=9876543210",
            ExternalId = "TESTISRC",
            IsAlbum = false
        };

        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];
        SetupRecordList( [("at://two-native-one-fallback", record)] );

        IEnumerable<SupportedProviders>? capturedProviders = null;
        _ = _sagaManagerMock
            .Setup( m => m.InitializeProviderStatesAsync(
                It.IsAny<string>( ), It.IsAny<IEnumerable<SupportedProviders>>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, IEnumerable<SupportedProviders>, CancellationToken>(
                ( _, providers, _ ) => capturedProviders = [.. providers] )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsNotNull( capturedProviders );
        HashSet<SupportedProviders> registered = [.. capturedProviders];
        CollectionAssert.AreEquivalent(
            _enabledProviders.ToList( ),
            registered.ToList( ),
            "All three providers should be registered when each has a leg" );
    }

    /// <summary>
    /// A missing provider with no external id produces no leg and therefore is absent from the
    /// registered set, so it cannot block IsComplete.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_ProviderWithNoLeg_IsNotRegistered( ) {
        // Only Spotify present + parseable; no external id; Tidal enabled but no leg.
        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = string.Empty,
            IsAlbum = false
        };

        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.Tidal];
        SetupRecordList( [("at://spotify-only", record)] );

        IEnumerable<SupportedProviders>? capturedProviders = null;
        _ = _sagaManagerMock
            .Setup( m => m.InitializeProviderStatesAsync(
                It.IsAny<string>( ), It.IsAny<IEnumerable<SupportedProviders>>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, IEnumerable<SupportedProviders>, CancellationToken>(
                ( _, providers, _ ) => capturedProviders = [.. providers] )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsNotNull( capturedProviders );
        HashSet<SupportedProviders> registered = [.. capturedProviders];
        Assert.DoesNotContain( SupportedProviders.Tidal, registered,
            "Tidal has no leg and must not be registered" );
        Assert.Contains( SupportedProviders.Spotify, registered );
    }

    #endregion

    #region Saga Keying Tests

    /// <summary>
    /// A track with ISRC seeds the saga with IsrcLookup key and type (fan-out suppressed).
    /// The saga id matches what an interactive ISRC lookup for the same entity produces (dedup parity).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueRecord_TrackWithIsrc_SeedsSagaWithIsrcKeyAndType( ) {
        const string Isrc = "testisrc123";
        SetupRecordList( [MakeStaleRecord( "at://track", DateTime.UtcNow.AddDays( -60 ), isrc: Isrc )] );

        string? capturedLookupKey = null;
        LookupRequestType capturedLookupType = default;
        string? capturedSagaId = null;

        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), It.IsAny<QueuePriority?>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( sagaId, lookupKey, lookupType, _, _, _ ) => {
                    capturedSagaId = sagaId;
                    capturedLookupKey = lookupKey;
                    capturedLookupType = lookupType;
                } )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsNotNull( capturedLookupKey );
        Assert.AreEqual( LookupRequestType.IsrcLookup, capturedLookupType );
        Assert.IsTrue( capturedLookupKey.StartsWith( "IsrcLookup:", StringComparison.Ordinal ) );

        // Dedup parity: the saga id must equal the one an interactive ISRC lookup would produce.
        string expectedKey = $"IsrcLookup:{Isrc.ToUpperInvariant( )}";
        string expectedSagaId = ISagaStateManager.GenerateSagaId( expectedKey );
        Assert.AreEqual( expectedSagaId, capturedSagaId );
    }

    /// <summary>
    /// An album with UPC seeds the saga with UpcLookup key and type.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueRecord_AlbumWithUpc_SeedsSagaWithUpcKeyAndType( ) {
        MediaLinkResult albumRecord = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        albumRecord.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/album/3TESTID111",
            ExternalId = "UPC98765",
            IsAlbum = true
        };

        SetupRecordList( [("at://album", albumRecord)] );

        LookupRequestType capturedLookupType = default;
        string? capturedLookupKey = null;

        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), It.IsAny<QueuePriority?>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( _, key, type, _, _, _ ) => { capturedLookupKey = key; capturedLookupType = type; } )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.AreEqual( LookupRequestType.UpcLookup, capturedLookupType );
        Assert.IsTrue( capturedLookupKey!.StartsWith( "UpcLookup:", StringComparison.Ordinal ) );
    }

    /// <summary>
    /// A record with parseable URLs but no external id: saga key derived from the first native leg
    /// in provider-enum order; stable across calls.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueRecord_NoExternalId_SeedsSagaFromFirstNativeLegDeterministically( ) {
        // Two records with identical URLs but no ISRC — saga ids must match across both.
        MediaLinkResult record1 = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record1.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID99999",
            ExternalId = string.Empty,
            IsAlbum = false
        };
        MediaLinkResult record2 = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -55 ) };
        record2.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID99999",
            ExternalId = string.Empty,
            IsAlbum = false
        };

        SetupRecordList( [("at://r1", record1), ("at://r2", record2)] );

        List<string> capturedSagaIds = [];
        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), It.IsAny<QueuePriority?>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( sagaId, _, _, _, _, _ ) => capturedSagaIds.Add( sagaId ) )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, capturedSagaIds );
        Assert.AreEqual( capturedSagaIds[0], capturedSagaIds[1],
            "Same native key should produce the same saga id across passes" );
    }

    /// <summary>
    /// The seeded LookupType is always external-id-typed (IsrcLookup or UpcLookup) when an external
    /// id exists — the precondition that makes the coordinator's secondary fan-out suppress.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueRecord_SagaLookupType_IsExternalIdTyped_ToSuppressFanout( ) {
        SetupRecordList( [MakeStaleRecord( "at://track", DateTime.UtcNow.AddDays( -60 ), isrc: "FANOUTSUPPRESSTEST" )] );

        LookupRequestType capturedLookupType = default;
        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), It.IsAny<QueuePriority?>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( _, _, type, _, _, _ ) => capturedLookupType = type )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsTrue(
            capturedLookupType is LookupRequestType.IsrcLookup or LookupRequestType.UpcLookup,
            $"Saga LookupType must be external-id-typed to suppress coordinator fan-out; got {capturedLookupType}" );
    }

    /// <summary>
    /// Two records that resolve to the same lookup key produce the same saga id.
    /// GetOrCreateAsync is called with Bulk origin priority.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_TwoRecordsSameLookupKey_ProduceSameSagaId( ) {
        const string SharedIsrc = "SHARED_ISRC_123";

        (string, MediaLinkResult) rec1 = MakeStaleRecord( "at://r1", DateTime.UtcNow.AddDays( -60 ), isrc: SharedIsrc );
        (string, MediaLinkResult) rec2 = MakeStaleRecord( "at://r2", DateTime.UtcNow.AddDays( -50 ), isrc: SharedIsrc );
        SetupRecordList( [rec1, rec2] );

        List<string> capturedSagaIds = [];
        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), It.IsAny<QueuePriority?>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( sagaId, _, _, _, _, _ ) => capturedSagaIds.Add( sagaId ) )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, capturedSagaIds );
        Assert.AreEqual( capturedSagaIds[0], capturedSagaIds[1],
            "Both records must produce the same deterministic sagaId" );

        _sagaManagerMock.Verify(
            m => m.GetOrCreateAsync(
                It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ), QueuePriority.Bulk, It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    #endregion

    #region Fire-and-Forget Tests

    /// <summary>
    /// The refresh pass enqueues and never polls saga completion — <c>GetAsync</c> is never called.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_DoesNotWaitForSagaCompletion( ) {
        SetupRecordList( [
            MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ), isrc: "ISRC_ONE" ),
            MakeStaleRecord( "at://test/2", DateTime.UtcNow.AddDays( -50 ), isrc: "ISRC_TWO" )
        ] );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );

        _sagaManagerMock.Verify(
            m => m.GetAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    #endregion

    #region Reliability Tests

    /// <summary>
    /// When enqueuing one record throws, it is counted as an error and skipped; the remaining
    /// records are still enqueued.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_PerRecordException_SkipsAndContinues( ) {
        (string, MediaLinkResult) rec1 = MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ), isrc: "ISRC_ONE" );
        (string, MediaLinkResult) rec2 = MakeStaleRecord( "at://test/2", DateTime.UtcNow.AddDays( -50 ), isrc: "ISRC_TWO" );
        (string, MediaLinkResult) rec3 = MakeStaleRecord( "at://test/3", DateTime.UtcNow.AddDays( -40 ), isrc: "ISRC_THREE" );
        SetupRecordList( [rec1, rec2, rec3] );

        int callCount = 0;
        _ = _queueMock
            .Setup( q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns<QueuedLookupRequest, QueuePriority, CancellationToken>( ( _, _, _ ) => {
                int count = System.Threading.Interlocked.Increment( ref callCount );
                if (count == 2) {
                    throw new InvalidOperationException( "Simulated enqueue failure" );
                }
                return Task.CompletedTask;
            } );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.AreEqual( 3, callCount );
    }

    /// <summary>
    /// When the PDS stream throws fatally mid-pass, the error is logged and the loop survives to
    /// the next tick.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_FatalMidPassError_LogsAndLoopSurvives( ) {
        // Use a very short interval so two ticks fire quickly, but the service still needs
        // to survive the grace period; we start with a 1-ms grace override isn't possible
        // through settings, so we drive RunRefreshPassAsync directly.
        int listCallCount = 0;
        TaskCompletionSource secondCallTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );

        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns<Uri, string, CancellationToken>( ( _, _, _ ) => {
                int count = System.Threading.Interlocked.Increment( ref listCallCount );
                if (count == 1) {
                    throw new InvalidOperationException( "Fatal pass error" );
                }
                _ = secondCallTcs.TrySetResult( );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        // Drive two passes directly to verify error isolation.
        StaleCacheRefreshBackgroundService service = CreateService( );

        // First pass — throws.
        await service.RunRefreshPassAsync( TestContext.CancellationToken );
        // Second pass — succeeds.
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsGreaterThanOrEqualTo( listCallCount, 2, "Both passes should attempt ListAllRecordsAsync" );
    }

    /// <summary>
    /// When the host cancels the service, the loop exits cleanly with no exception.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully( ) {
        StaleCacheRefreshBackgroundService service = CreateService( );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );
        Task executeTask = service.StartAsync( cts.Token );
        await cts.CancelAsync( );

        await executeTask; // must not throw
    }

    #endregion

    #region Leg Enqueue Failure Tests

    /// <summary>
    /// When one provider's queue throws on EnqueueAsync, that provider's state is recorded as
    /// complete-and-failed in the saga; the other legs are still enqueued; and SetIsPartialAsync
    /// is never called.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueRecordAsync_OneProviderQueueThrows_MarksFailedLegAndEnqueuesOthers( ) {
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic];

        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = "ISRC_TWOLEG",
            IsAlbum = false
        };
        record.Results[SupportedProviders.AppleMusic] = new MusicLookupResult {
            URL = "https://music.apple.com/us/album/x/1234567890?i=9876543210",
            ExternalId = "ISRC_TWOLEG",
            IsAlbum = false
        };

        SetupRecordList( [("at://two-leg", record)] );

        Mock<IRequestQueue<QueuedLookupRequest>> spotifyQueue = new( );
        Mock<IRequestQueue<QueuedLookupRequest>> appleQueue = new( );

        _ = spotifyQueue
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Simulated Spotify queue failure" ) );
        _ = appleQueue
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        _ = _queueResolverMock.Setup( r => r.GetQueue( SupportedProviders.Spotify ) ).Returns( spotifyQueue.Object );
        _ = _queueResolverMock.Setup( r => r.GetQueue( SupportedProviders.AppleMusic ) ).Returns( appleQueue.Object );

        List<ProviderLookupState> recordedStates = [];
        _ = _sagaManagerMock
            .Setup( m => m.UpdateProviderStateAsync(
                It.IsAny<string>( ),
                It.IsAny<ProviderLookupState>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<string, ProviderLookupState, CancellationToken>( ( _, state, _ ) => recordedStates.Add( state ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        // Spotify leg failed: state recorded as complete-as-failed.
        ProviderLookupState? spotifyState = recordedStates.FirstOrDefault( s => s.Provider == SupportedProviders.Spotify );
        Assert.IsNotNull( spotifyState, "A failed provider state must be recorded for the throwing provider" );
        Assert.IsTrue( spotifyState.IsComplete, "Failed leg must be marked IsComplete=true" );
        Assert.IsFalse( spotifyState.IsSuccess, "Failed leg must be marked IsSuccess=false" );

        // Apple leg succeeded: EnqueueAsync called once, no UpdateProviderStateAsync for Apple.
        appleQueue.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "The non-failing leg must still be enqueued" );
        Assert.DoesNotContain( s => s.Provider == SupportedProviders.AppleMusic, recordedStates,
            "A successful leg must not be recorded as failed" );

        // SetIsPartialAsync must never be called.
        _sagaManagerMock.Verify(
            m => m.SetIsPartialAsync( It.IsAny<string>( ), It.IsAny<bool>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "SetIsPartialAsync must not be called when a leg fails; marking the provider complete-as-failed is sufficient" );
    }

    /// <summary>
    /// When all provider queues throw on EnqueueAsync, all legs are marked complete-as-failed in
    /// the saga; the method returns true (the record was dispatched — the saga will be finalized
    /// via the coordinator poll backstop); and SetIsPartialAsync is never called.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueRecordAsync_AllProviderQueuesThrow_AllLegsMarkedFailedAndReturnsTrue( ) {
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic];

        MediaLinkResult record = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        record.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = "ISRC_ALLTHROW",
            IsAlbum = false
        };
        record.Results[SupportedProviders.AppleMusic] = new MusicLookupResult {
            URL = "https://music.apple.com/us/album/x/1234567890?i=9876543210",
            ExternalId = "ISRC_ALLTHROW",
            IsAlbum = false
        };

        SetupRecordList( [("at://all-throw", record)] );

        _ = _queueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "All queues down" ) );

        List<ProviderLookupState> recordedStates = [];
        _ = _sagaManagerMock
            .Setup( m => m.UpdateProviderStateAsync(
                It.IsAny<string>( ),
                It.IsAny<ProviderLookupState>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<string, ProviderLookupState, CancellationToken>( ( _, state, _ ) => recordedStates.Add( state ) )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );

        // RunRefreshPassAsync must complete without throwing and count the record as enqueued.
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        // Both legs recorded as complete-as-failed.
        Assert.HasCount( 2, recordedStates,
            "All failing legs must have their provider state recorded" );
        Assert.IsTrue( recordedStates.All( s => s.IsComplete ),
            "All failing legs must be marked IsComplete=true" );
        Assert.IsTrue( recordedStates.All( s => !s.IsSuccess ),
            "All failing legs must be marked IsSuccess=false" );

        // SetIsPartialAsync must never be called.
        _sagaManagerMock.Verify(
            m => m.SetIsPartialAsync( It.IsAny<string>( ), It.IsAny<bool>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "SetIsPartialAsync must not be called when all legs fail" );
    }

    #endregion

    #region AppHost Branches Tests

    /// <summary>
    /// Both the production (AddProductionExecutable) and development (AddProject) branches of the
    /// AppHost cache-bootstrap wiring inject the new refresh tunables and the retuned defaults.
    /// Each tunable is asserted to appear in EACH branch sub-string individually, so a tunable
    /// injected twice in the prod branch but zero times in the dev branch is caught — the silent-revert
    /// trap where the whole-file count of 2 would previously pass.
    /// </summary>
    [TestMethod]
    public void AppHost_BothCacheBootstrapBranches_InjectNewTunables( ) {
        // Locate the AppHost Program.cs relative to the test assembly.
        // Assembly → Tests/bin/Debug/net10.0 → walk up to repo root → src/BridgeBeats.AppHost/Program.cs
        string assemblyDir = System.IO.Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly( ).Location )!;

        string? repoRoot = assemblyDir;
        while (repoRoot is not null && !System.IO.File.Exists( System.IO.Path.Combine( repoRoot, "BridgeBeats.sln" ) )) {
            repoRoot = System.IO.Path.GetDirectoryName( repoRoot );
        }

        if (repoRoot is null) {
            Assert.Inconclusive( "Could not locate BridgeBeats.sln from the test assembly path; skipping AppHost source assertion." );
            return;
        }

        string appHostPath = System.IO.Path.Combine( repoRoot, "src", "BridgeBeats.AppHost", "Program.cs" );
        Assert.IsTrue( System.IO.File.Exists( appHostPath ),
            $"AppHost Program.cs not found at expected path: {appHostPath}" );

        string source = System.IO.File.ReadAllText( appHostPath );

        // Locate the cache-bootstrap production and development branch sub-strings by their unique
        // markers. The production branch is identified by its AddProductionExecutable call for
        // "cache-bootstrap"; the development branch is identified by the AddProject call for the
        // same worker. Splitting at these two markers yields two non-overlapping sub-strings, one
        // per branch. Asserting each tunable appears in each sub-string catches the silent-revert
        // trap: a tunable removed from one branch but still present in the other still has a
        // whole-file count >= 1, but fails the per-branch assertion.
        const string ProdBranchMarker = "AddProductionExecutable( \"cache-bootstrap\"";
        const string DevBranchMarker  = "AddProject<Projects.BridgeBeats_Worker_CacheBootstrap>( \"cache-bootstrap\" )";

        int prodIdx = source.IndexOf( ProdBranchMarker, StringComparison.Ordinal );
        int devIdx  = source.IndexOf( DevBranchMarker,  StringComparison.Ordinal );

        if (prodIdx < 0 || devIdx < 0 || prodIdx >= devIdx) {
            Assert.Inconclusive(
                "Could not locate the distinct cache-bootstrap prod/dev branch markers in AppHost Program.cs. " +
                "The source structure may have changed; update the marker constants in this test." );
            return;
        }

        // prod sub-string: from the prod marker to the start of the dev marker.
        string prodBranch = source.Substring( prodIdx, devIdx - prodIdx );
        // dev sub-string: from the dev marker to end-of-file (nothing after dev branch for cache-bootstrap).
        string devBranch  = source.Substring( devIdx );

        string[] requiredEnvVars = [
            "BridgeBeats__RefreshIntervalHours",
            "BridgeBeats__MaxRecordsPerRun",
        ];

        foreach (string envVar in requiredEnvVars) {
            Assert.IsTrue( prodBranch.Contains( envVar, StringComparison.Ordinal ),
                $"'{envVar}' must appear in the production cache-bootstrap branch (AddProductionExecutable). " +
                $"A missing occurrence means the prod branch silently omits the tunable." );

            Assert.IsTrue( devBranch.Contains( envVar, StringComparison.Ordinal ),
                $"'{envVar}' must appear in the development cache-bootstrap branch (AddProject). " +
                $"A missing occurrence means the dev branch silently omits the tunable." );
        }
    }

    #endregion

    #region Settings Binding Tests

    /// <summary>
    /// Settings with unset env falls back to 6h interval and 500 max records
    /// (the defaults verified via the settings record constructor).
    /// </summary>
    [TestMethod]
    public void Settings_DefaultValues_MatchSpecifiedDefaults( ) {
        CacheBootstrapSettings defaults = MakeSettings( );

        Assert.AreEqual( TimeSpan.FromHours( 6 ), defaults.RefreshInterval );
        Assert.AreEqual( 500, defaults.MaxRecordsPerRun );
    }

    /// <summary>
    /// The settings record stores exactly the values it is constructed with.
    /// </summary>
    [TestMethod]
    public void Settings_StoresProvidedValues( ) {
        CacheBootstrapSettings settings = new(
            s_testPdsUri,
            TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 12 ),
            MaxRecordsPerRun: 250
        );

        Assert.AreEqual( TimeSpan.FromHours( 12 ), settings.RefreshInterval );
        Assert.AreEqual( 250, settings.MaxRecordsPerRun );
    }

    #endregion

    #region No-Drip Regression Guard

    /// <summary>
    /// A multi-record pass enqueues every leg without any artificial inter-leg delay: all 6 legs
    /// (2 records × 3 providers) are enqueued, and the entire pass completes within 5 seconds.
    /// The wall-clock bound is a re-introduction guard, not a performance benchmark; it would be
    /// exceeded if any per-leg delay were restored.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_MultiRecord_EnqueuesAllLegsWithoutDelay( ) {
        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        SetupRecordList( [
            ("at://rec1", MakeThreeProviderRecord( isrc: "ISRC_ONE" )),
            ("at://rec2", MakeThreeProviderRecord( isrc: "ISRC_TWO" ))
        ] );

        StaleCacheRefreshBackgroundService service = CreateService( );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );
        cts.CancelAfter( TimeSpan.FromSeconds( 5 ) );

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew( );
        await service.RunRefreshPassAsync( cts.Token );
        sw.Stop( );

        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 6 ),
            "2 records × 3 providers = 6 legs must all be enqueued" );

        Assert.IsLessThan( TimeSpan.FromSeconds( 5 ), sw.Elapsed,
            "The pass must complete in under 5 seconds; any per-leg delay would exceed this bound." );
    }

    #endregion

    #region Helper Methods

    private StaleCacheRefreshBackgroundService CreateService( ) =>
        new(
            _atProtoStorageMock.Object,
            _sagaManagerMock.Object,
            _queueResolverMock.Object,
            _redisMock.Object,
            _enabledProviders,
            _settings,
            _loggerMock.Object
        );

    private CacheBootstrapSettings MakeSettings(
        TimeSpan? refreshInterval = null,
        int maxRecordsPerRun = 500,
        int cacheDays = 30
    ) =>
        new(
            s_testPdsUri,
            TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: cacheDays,
            RefreshInterval: refreshInterval ?? TimeSpan.FromHours( 6 ),
            MaxRecordsPerRun: maxRecordsPerRun
        );

    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    private static (string AtUri, MediaLinkResult Result) MakeStaleRecord(
        string atUri,
        DateTime lookedUpAt,
        string isrc = "ISRC_DEFAULT",
        bool forceIsPartial = false
    ) {
        MediaLinkResult result = new( ) { LookedUpAt = lookedUpAt, IsPartial = forceIsPartial };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = isrc,
            Title = "Stale Song",
            Artist = "Stale Artist",
            IsAlbum = false
        };
        return (atUri, result);
    }

    private static (string AtUri, MediaLinkResult Result) MakeFreshRecord(
        string atUri,
        DateTime lookedUpAt,
        string isrc = "FRESH_ISRC_DEFAULT"
    ) {
        MediaLinkResult result = new( ) { LookedUpAt = lookedUpAt, IsPartial = false };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTFRESH1",
            ExternalId = isrc,
            Title = "Fresh Song",
            Artist = "Fresh Artist",
            IsAlbum = false
        };
        return (atUri, result);
    }

    /// <summary>
    /// Creates a record with parseable URLs for all three providers and the given ISRC.
    /// </summary>
    private static MediaLinkResult MakeThreeProviderRecord( string isrc ) {
        MediaLinkResult result = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            URL = "https://open.spotify.com/track/3SPOTID12345",
            ExternalId = isrc,
            IsAlbum = false,
            Title = "Test",
            Artist = "Artist"
        };
        result.Results[SupportedProviders.AppleMusic] = new MusicLookupResult {
            URL = "https://music.apple.com/us/album/something/1234567890?i=9876543210",
            ExternalId = isrc,
            IsAlbum = false,
            Title = "Test",
            Artist = "Artist"
        };
        result.Results[SupportedProviders.Tidal] = new MusicLookupResult {
            URL = "https://tidal.com/browse/track/12345678",
            ExternalId = isrc,
            IsAlbum = false,
            Title = "Test",
            Artist = "Artist"
        };
        return result;
    }

    private static LookupSagaState MakeMinimalSaga( ) =>
        new( ) {
            SagaId = "minisaga",
            LookupKey = "test:key",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = "TESTISRC",
            OriginPriority = QueuePriority.Bulk
        };

    #endregion
}
