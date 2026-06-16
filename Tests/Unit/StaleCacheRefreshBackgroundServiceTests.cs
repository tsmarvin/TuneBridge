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
/// Tests <see cref="StaleCacheRefreshBackgroundService"/>, which periodically selects the oldest
/// stale cached media-link records and bulk-enqueues them for re-lookup. Verifies the cadence
/// (first tick is deferred), the skip guard (bootstrap running), bounded selection, entry
/// derivation, bulk-priority enforcement, fire-and-forget behavior, per-record resilience,
/// cancellation, and deterministic saga keying.
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

        _enabledProviders = [SupportedProviders.Spotify];

        _settings = new CacheBootstrapSettings(
            s_testPdsUri,
            TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 100
        );
    }

    #region Cadence Tests

    /// <summary>
    /// At service startup (t=0) <c>ListAllRecordsAsync</c> must NOT be called because the first
    /// tick of the <see cref="PeriodicTimer"/> fires only after one full interval — the deliberate
    /// inversion of the immediate-run pattern in <see cref="CacheBootstrapBackgroundService"/>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_AtStartup_DoesNotCallListAllRecords( ) {
        // Arrange: use a very long interval so the timer never fires during the test
        _settings = new CacheBootstrapSettings(
            s_testPdsUri, TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 100
        );
        StaleCacheRefreshBackgroundService service = CreateService( );

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );

        Task executeTask = service.StartAsync( cts.Token );

        // Wait briefly then cancel — first tick has not fired
        await Task.Delay( 200, TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        _atProtoStorageMock.Verify(
            s => s.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    #endregion

    #region Skip Guard Tests

    /// <summary>
    /// When the bootstrap status document reports <c>IsRunning = true</c>, the refresh pass must
    /// skip selection and enqueue no records. A pass with <c>IsRunning = false</c> proceeds normally.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WhenBootstrapIsRunning_SkipsSelectionAndEnqueue( ) {
        // Arrange: status document says bootstrap is in progress
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
    /// When the bootstrap status document reports <c>IsRunning = false</c>, the refresh pass proceeds
    /// with selection and enqueuing.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WhenBootstrapIsNotRunning_ProceedsWithSelection( ) {
        // Arrange: status says not running, one stale record
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
    /// When the bootstrap status is absent from Redis (null), the pass treats it as not-running and
    /// proceeds.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_WhenStatusAbsent_ProceedsWithSelection( ) {
        // Arrange: Redis returns Null (no status document)
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

    #region Selection Tests

    /// <summary>
    /// When more than N stale records exist, exactly N are enqueued AND they are the N oldest by
    /// <c>LookedUpAt</c>; the newest-stale record is NOT enqueued.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_MoreThanN_EnqueuesExactlyNOldest( ) {
        // Arrange: N=2, three stale records with distinct timestamps
        _settings = new CacheBootstrapSettings(
            s_testPdsUri, TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 2
        );

        DateTime oldest = DateTime.UtcNow.AddDays( -90 );
        DateTime middle = DateTime.UtcNow.AddDays( -60 );
        DateTime newest = DateTime.UtcNow.AddDays( -31 ); // still stale, but newest

        (string, MediaLinkResult) rec1 = MakeStaleRecord( "at://oldest", oldest, isrc: "ISRC_OLDEST" );
        (string, MediaLinkResult) rec2 = MakeStaleRecord( "at://middle", middle, isrc: "ISRC_MIDDLE" );
        (string, MediaLinkResult) rec3 = MakeStaleRecord( "at://newest", newest, isrc: "ISRC_NEWEST" );

        // Stream order differs from timestamp order: feed middle, newest, oldest so that a
        // take-first-N bug would select {middle, newest} and fail the assertions below.
        SetupRecordList( [rec2, rec3, rec1] );

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

        // Assert: exactly N=2 enqueued
        Assert.HasCount( 2, enqueuedRequests, "Expected exactly N=2 records enqueued" );

        // Assert: the N oldest were selected (ISRC_OLDEST and ISRC_MIDDLE)
        IEnumerable<string> enqueuedValues = enqueuedRequests.Select( r => r.LookupValue );
        CollectionAssert.Contains( enqueuedValues.ToList( ), "ISRC_OLDEST" );
        CollectionAssert.Contains( enqueuedValues.ToList( ), "ISRC_MIDDLE" );

        // Assert: the newest-stale was NOT enqueued
        CollectionAssert.DoesNotContain( enqueuedValues.ToList( ), "ISRC_NEWEST" );
    }

    /// <summary>
    /// When fewer than N stale records exist, all stale records are enqueued and the service does
    /// not throw.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_FewerThanN_EnqueuesAllStale( ) {
        _settings = new CacheBootstrapSettings(
            s_testPdsUri, TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 10
        );

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
    /// A record whose <c>LookedUpAt</c> is one tick older than the cache window IS selected as stale;
    /// a record one tick newer than the window is NOT selected.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_BoundaryTimestamps_SelectsOlderNotNewer( ) {
        int cacheDays = 30;
        _settings = new CacheBootstrapSettings(
            s_testPdsUri, TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: cacheDays,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 100
        );

        // One second beyond the window (stale)
        DateTime justStale = DateTime.UtcNow.AddDays( -cacheDays ).AddSeconds( -1 );
        // One day inside the window (clearly fresh)
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
        Assert.AreEqual( "STALE_ISRC", enqueuedRequests[0].LookupValue );
    }

    /// <summary>
    /// A record with <c>IsPartial = true</c> and a fresh timestamp IS selected as stale; a record
    /// with <c>IsPartial = false</c> and a fresh timestamp is NOT selected.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_IsPartialTrueWithFreshTimestamp_IsSelectedAsStale( ) {
        DateTime fresh = DateTime.UtcNow.AddDays( -1 ); // well within cache window

        // IsPartial=true with fresh timestamp — stale
        (string, MediaLinkResult) partialRec = MakeStaleRecord( "at://partial", fresh, isrc: "PARTIAL_ISRC", forceIsPartial: true );
        // IsPartial=false with fresh timestamp — NOT stale
        (string, MediaLinkResult) freshRec = MakeFreshRecord( "at://fresh", fresh, isrc: "FRESH_ISRC" );

        SetupRecordList( [partialRec, freshRec] );

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
        Assert.AreEqual( "PARTIAL_ISRC", enqueuedRequests[0].LookupValue );
    }

    #endregion

    #region Entry Derivation Tests

    /// <summary>
    /// ExternalId present and <c>IsAlbum = true</c> yields a <c>UpcLookup</c>.
    /// </summary>
    [TestMethod]
    public void DeriveEntryParams_ExternalIdWithIsAlbumTrue_YieldsUpcLookup( ) {
        MediaLinkResult result = MakeResultWithExternalId( "UPC12345", isAlbum: true );

        (LookupRequestType type, string key, string value, bool album, string? _, string? __) =
            StaleCacheRefreshBackgroundService.DeriveEntryParams( result );

        Assert.AreEqual( LookupRequestType.UpcLookup, type );
        Assert.AreEqual( $"UpcLookup:UPC12345", key );
        Assert.AreEqual( "UPC12345", value );
        Assert.IsTrue( album );
    }

    /// <summary>
    /// A lowercase UPC is preserved as-is after trimming — the album/UPC branch does NOT uppercase
    /// the value. Case normalisation for UPCs happens later in <c>GenerateSagaId</c>.
    /// </summary>
    [TestMethod]
    public void DeriveEntryParams_LowercaseUpcWithIsAlbumTrue_DoesNotUppercase( ) {
        MediaLinkResult result = MakeResultWithExternalId( "  upc98765  ", isAlbum: true );

        (LookupRequestType type, string key, string value, bool album, string? _, string? __) =
            StaleCacheRefreshBackgroundService.DeriveEntryParams( result );

        Assert.AreEqual( LookupRequestType.UpcLookup, type );
        Assert.AreEqual( "UpcLookup:upc98765", key );
        Assert.AreEqual( "upc98765", value ); // trimmed, but NOT uppercased
        Assert.IsTrue( album );
    }

    /// <summary>
    /// ExternalId present and <c>IsAlbum = false</c> yields an <c>IsrcLookup</c> with the external
    /// id uppercased and trimmed.
    /// </summary>
    [TestMethod]
    public void DeriveEntryParams_ExternalIdWithIsAlbumFalse_YieldsIsrcLookup( ) {
        MediaLinkResult result = MakeResultWithExternalId( "isrc_abc123", isAlbum: false );

        (LookupRequestType type, string key, string value, bool album, string? _, string? __) =
            StaleCacheRefreshBackgroundService.DeriveEntryParams( result );

        Assert.AreEqual( LookupRequestType.IsrcLookup, type );
        Assert.AreEqual( $"IsrcLookup:ISRC_ABC123", key );
        Assert.AreEqual( "ISRC_ABC123", value );
        Assert.IsFalse( album );
    }

    /// <summary>
    /// ExternalId present and <c>IsAlbum = null</c> treats null as not-album and yields an
    /// <c>IsrcLookup</c> (null ?? false = false).
    /// </summary>
    [TestMethod]
    public void DeriveEntryParams_ExternalIdWithIsAlbumNull_YieldsIsrcLookup( ) {
        MediaLinkResult result = MakeResultWithExternalId( "ISRC_NULL_ALBUM", isAlbum: null );

        (LookupRequestType type, string key, string value, bool album, string? _, string? __) =
            StaleCacheRefreshBackgroundService.DeriveEntryParams( result );

        Assert.AreEqual( LookupRequestType.IsrcLookup, type );
        Assert.AreEqual( $"IsrcLookup:ISRC_NULL_ALBUM", key );
        Assert.IsFalse( album );
    }

    /// <summary>
    /// When no result has an ExternalId but one has both Title and Artist, a <c>SongLookup</c> is
    /// produced with the normalized key and the raw <c>"{title}|{artist}"</c> lookup value.
    /// </summary>
    [TestMethod]
    public void DeriveEntryParams_NoExternalIdButTitleAndArtist_YieldsSongLookup( ) {
        MediaLinkResult result = new( );
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            Title = "My Song",
            Artist = "My Artist",
            ExternalId = string.Empty
        };

        (LookupRequestType type, string key, string value, bool album, string? title, string? artist) =
            StaleCacheRefreshBackgroundService.DeriveEntryParams( result );

        Assert.AreEqual( LookupRequestType.SongLookup, type );
        Assert.AreEqual( "SongLookup:MY SONG:MY ARTIST", key );
        Assert.AreEqual( "My Song|My Artist", value );
        Assert.IsFalse( album );
        Assert.AreEqual( "My Song", title );
        Assert.AreEqual( "My Artist", artist );
    }

    /// <summary>
    /// When no result has an ExternalId AND no result has both Title and Artist, the record cannot
    /// be enqueued: the returned <c>lookupKey</c> is empty and <c>EnqueueAsync</c> is never called.
    /// This is a skip, not an error.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_RecordWithNoIdentifier_IsSkippedAndNotAnError( ) {
        // Arrange: a record with no ExternalId and no Title/Artist
        MediaLinkResult result = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ), IsPartial = false };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            ExternalId = string.Empty,
            Title = string.Empty,
            Artist = string.Empty
        };

        SetupRecordList( [("at://noid", result)] );
        StaleCacheRefreshBackgroundService service = CreateService( );

        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        // EnqueueAsync must never be called for this record
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );

        // No error-level log should be emitted for a clean skip
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

    #region Bulk Priority Tests

    /// <summary>
    /// Every enqueued request must carry <c>OriginPriority = QueuePriority.Bulk</c> AND the
    /// <c>EnqueueAsync</c> priority argument must also be <c>QueuePriority.Bulk</c>.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_AllRequests_HaveBulkOriginAndEnqueuePriority( ) {
        SetupRecordList( [MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ) )] );

        QueuedLookupRequest? capturedRequest = null;
        QueuePriority capturedPriority = QueuePriority.Interactive; // wrong default — must be overwritten

        _ = _queueMock
            .Setup( q => q.EnqueueAsync(
                It.IsAny<QueuedLookupRequest>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<QueuedLookupRequest, QueuePriority, CancellationToken>(
                ( req, pri, _ ) => { capturedRequest = req; capturedPriority = pri; } )
            .Returns( Task.CompletedTask );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.IsNotNull( capturedRequest );
        Assert.AreEqual( QueuePriority.Bulk, capturedRequest.OriginPriority,
            "QueuedLookupRequest.OriginPriority must be Bulk" );
        Assert.AreEqual( QueuePriority.Bulk, capturedPriority,
            "EnqueueAsync priority argument must be Bulk" );
    }

    /// <summary>
    /// <c>EnqueueAsync</c> must never be called with <c>QueuePriority.Interactive</c> — the
    /// negative control for bulk-priority enforcement.
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

    #region Fire-and-Forget Tests

    /// <summary>
    /// The refresh pass enqueues and never polls saga completion — <c>GetAsync</c> is never called.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_DoesNotWaitForSagaCompletion( ) {
        // Arrange: two records; the pass enqueues and never polls saga completion
        (string, MediaLinkResult) rec1 = MakeStaleRecord( "at://test/1", DateTime.UtcNow.AddDays( -60 ), isrc: "ISRC_ONE" );
        (string, MediaLinkResult) rec2 = MakeStaleRecord( "at://test/2", DateTime.UtcNow.AddDays( -50 ), isrc: "ISRC_TWO" );
        SetupRecordList( [rec1, rec2] );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        // The pass completed — both records were enqueued
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );

        // Service must never call a saga-completion / result getter
        _sagaManagerMock.Verify(
            m => m.GetAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    #endregion

    #region Reliability Tests

    /// <summary>
    /// When enqueuing one record throws, it is counted as an error and skipped; the remaining
    /// N-1 records are still enqueued.
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

        // All 3 records attempted; 1 failed, 2 succeeded (Times.Exactly(3) calls attempted)
        Assert.AreEqual( 3, callCount );
    }

    /// <summary>
    /// When the PDS stream throws fatally mid-pass, the error is logged and the loop survives to the
    /// next tick (no unhandled exception propagates).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_FatalMidPassError_LogsAndLoopSurvives( ) {
        // Arrange: use a very short interval so two ticks fire quickly
        _settings = new CacheBootstrapSettings(
            s_testPdsUri, TestUserDid,
            BootstrapInterval: TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromMilliseconds( 50 ),
            MaxRecordsPerRun: 100
        );

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

        StaleCacheRefreshBackgroundService service = CreateService( );
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource( TestContext.CancellationToken );

        Task executeTask = service.StartAsync( cts.Token );

        await secondCallTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        Assert.IsGreaterThanOrEqualTo( listCallCount, 2, "Loop must survive a fatal error and attempt the next pass" );
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

    #region Idempotent Keying Tests

    /// <summary>
    /// Two records that resolve to the same <c>lookupKey</c> produce the same <c>sagaId</c>.
    /// <c>GetOrCreateAsync</c> is called with <c>originPriority: QueuePriority.Bulk</c> for both.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunRefreshPass_TwoRecordsSameLookupKey_ProduceSameSagaId( ) {
        const string SharedIsrc = "SHARED_ISRC_123";

        // Two records with identical ISRC — same lookupKey
        (string, MediaLinkResult) rec1 = MakeStaleRecord( "at://r1", DateTime.UtcNow.AddDays( -60 ), isrc: SharedIsrc );
        (string, MediaLinkResult) rec2 = MakeStaleRecord( "at://r2", DateTime.UtcNow.AddDays( -50 ), isrc: SharedIsrc );
        SetupRecordList( [rec1, rec2] );

        List<string> capturedSagaIds = [];
        _ = _sagaManagerMock
            .Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback<string, string, LookupRequestType, string, QueuePriority?, CancellationToken>(
                ( sagaId, _, _, _, _, _ ) => capturedSagaIds.Add( sagaId ) )
            .ReturnsAsync( MakeMinimalSaga( ) );

        StaleCacheRefreshBackgroundService service = CreateService( );
        await service.RunRefreshPassAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, capturedSagaIds );
        Assert.AreEqual( capturedSagaIds[0], capturedSagaIds[1], "Both records must produce the same deterministic sagaId" );

        // Both calls must use Bulk origin priority
        _sagaManagerMock.Verify(
            m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                QueuePriority.Bulk,
                It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
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

    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    /// <summary>
    /// Creates a stale record (old LookedUpAt) with an ExternalId ISRC for testing.
    /// </summary>
    private static (string AtUri, MediaLinkResult Result) MakeStaleRecord(
        string atUri,
        DateTime lookedUpAt,
        string isrc = "ISRC_DEFAULT",
        bool forceIsPartial = false
    ) {
        MediaLinkResult result = new( ) { LookedUpAt = lookedUpAt, IsPartial = forceIsPartial };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            ExternalId = isrc,
            Title = "Stale Song",
            Artist = "Stale Artist",
            IsAlbum = false
        };
        return (atUri, result);
    }

    /// <summary>
    /// Creates a fresh record (recent LookedUpAt, IsPartial=false) that should NOT be selected.
    /// </summary>
    private static (string AtUri, MediaLinkResult Result) MakeFreshRecord(
        string atUri,
        DateTime lookedUpAt,
        string isrc = "FRESH_ISRC_DEFAULT"
    ) {
        MediaLinkResult result = new( ) { LookedUpAt = lookedUpAt, IsPartial = false };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            ExternalId = isrc,
            Title = "Fresh Song",
            Artist = "Fresh Artist",
            IsAlbum = false
        };
        return (atUri, result);
    }

    private static MediaLinkResult MakeResultWithExternalId( string externalId, bool? isAlbum ) {
        MediaLinkResult result = new( ) { LookedUpAt = DateTime.UtcNow.AddDays( -60 ) };
        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            ExternalId = externalId,
            Title = "Test Song",
            Artist = "Test Artist",
            IsAlbum = isAlbum
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
