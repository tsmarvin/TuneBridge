using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.CacheBootstrap;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Tests <see cref="CacheBootstrapBackgroundService"/>, which rebuilds the Redis lookup index from the
/// source-of-truth PDS by streaming every stored record and re-registering its input-link pointers,
/// while publishing a status document to Redis.
/// </summary>
/// <remarks>
/// The tests drive the hosted service through <c>StartAsync</c> with mocked storage, cache, and Redis
/// dependencies, then cancel once the expected work has been observed. They cover construction, the
/// bootstrap pass (each record re-registered via <see cref="IMediaLinkCacheRepository.AddInputLinksAsync"/>),
/// resilience to per-record and whole-pass failures, graceful cancellation, the parameters passed to
/// <see cref="IATProtoStorageService.ListAllRecordsAsync"/>, settings storage, and the shape of the
/// status document written to <see cref="CacheBootstrapStatus.RedisKey"/> (including preservation of a
/// prior completed run's fields while a new run is in progress or has failed).
/// </remarks>
[TestClass]
public class CacheBootstrapBackgroundServiceTests {

    /// <summary>Mock PDS storage service that streams the records to re-index.</summary>
    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;

    /// <summary>Mock cache repository whose <c>AddInputLinksAsync</c> calls record the re-indexing.</summary>
    private Mock<IMediaLinkCacheRepository> _cacheRepositoryMock = null!;

    /// <summary>Mock Redis connection multiplexer handing out the mocked database.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;

    /// <summary>Mock Redis database used to observe status-document reads and writes.</summary>
    private Mock<IDatabase> _redisDatabaseMock = null!;

    /// <summary>Mock logger injected into the service.</summary>
    private Mock<ILogger<CacheBootstrapBackgroundService>> _loggerMock = null!;

    /// <summary>Settings (PDS URI, user DID, bootstrap interval) the service runs with.</summary>
    private CacheBootstrapSettings _settings = null!;

    /// <summary>MSTest-injected context, used to flow the test's cancellation token into awaits.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Test PDS URI passed to the service.</summary>
    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );

    /// <summary>Test user DID passed to the service.</summary>
    private const string TestUserDid = "did:plc:testuser123";

    /// <summary>
    /// Constructs the mocks and default settings before each test, wiring the Redis mock so the status
    /// document reads as absent and writes succeed unless a test overrides them.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _cacheRepositoryMock = new Mock<IMediaLinkCacheRepository>( );
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _redisDatabaseMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<CacheBootstrapBackgroundService>>( );

        // Setup Redis mock to return empty endpoints (avoids null reference in diagnostics)
        _ = _redisMock.Setup( r => r.GetEndPoints( It.IsAny<bool>( ) ) ).Returns( [] );

        // Wire database mock; default read returns null (no prior status)
        _ = _redisMock
            .Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _redisDatabaseMock.Object );
        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( RedisValue.Null );
        _ = _redisDatabaseMock
            .Setup( d => d.StringSetAsync( It.IsAny<RedisKey>( ), It.IsAny<RedisValue>( ), It.IsAny<Expiration>( ), It.IsAny<StackExchange.Redis.ValueCondition>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( true );

        _settings = new CacheBootstrapSettings(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 6 ),
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 100
        );
    }

    #region Constructor Tests

    /// <summary>Verifies that the service constructs successfully with valid dependencies.</summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        CacheBootstrapBackgroundService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    #endregion

    #region ExecuteAsync Tests

    /// <summary>
    /// Verifies that on startup the service streams every PDS record and re-registers each one through
    /// <see cref="IMediaLinkCacheRepository.AddInputLinksAsync"/> exactly once (here three records).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_OnStartup_ShouldBootstrapCache( ) {
        // Arrange
        List<(string AtUri, MediaLinkResult Result)> records = [
            CreateTestRecord( "at://test/1" ),
            CreateTestRecord( "at://test/2" ),
            CreateTestRecord( "at://test/3" )
        ];

        // Signal when all 3 records have been processed by AddInputLinksAsync
        TaskCompletionSource allProcessedTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        int[] callCount = [0];
        _ = _cacheRepositoryMock
            .Setup( x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ) )
            .Callback<string, MediaLinkResult>( ( _, _ ) => {
                if (System.Threading.Interlocked.Increment( ref callCount[0] ) >= 3) {
                    _ = allProcessedTcs.TrySetResult( );
                }
            } )
            .Returns( Task.CompletedTask );

        SetupRecordList( records );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        await allProcessedTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        // Assert - AddInputLinksAsync should be called for each record
        _cacheRepositoryMock.Verify(
            x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ),
            Times.Exactly( 3 )
        );
    }

    /// <summary>
    /// Verifies that when the PDS returns no records the pass completes without re-registering anything,
    /// so an empty repository is handled cleanly.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WithEmptyCollection_ShouldCompleteWithoutErrors( ) {
        // Arrange: signal when ListAllRecordsAsync is called (first run completed when it returns)
        TaskCompletionSource listCalledTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Callback<Uri, string, CancellationToken, bool>( ( _, _, _, _ ) => listCalledTcs.TrySetResult( ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );

        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        await listCalledTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        // Assert - No exceptions thrown, AddInputLinksAsync never called
        _cacheRepositoryMock.Verify(
            x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ),
            Times.Never( )
        );
    }

    /// <summary>
    /// Verifies that when re-registering one record throws, the service still attempts the remaining
    /// records, so a single bad record does not abort the whole bootstrap pass.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WhenRecordFails_ShouldContinueWithOtherRecords( ) {
        // Arrange
        List<(string AtUri, MediaLinkResult Result)> records = [
            CreateTestRecord( "at://test/1" ),
            CreateTestRecord( "at://test/2" ),
            CreateTestRecord( "at://test/3" )
        ];
        SetupRecordList( records );

        // Signal when the third AddInputLinksAsync call completes (all 3 attempted)
        TaskCompletionSource allAttemptedTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        int[] attemptCount = [0];

        _ = _cacheRepositoryMock
            .Setup( x => x.AddInputLinksAsync( "at://test/1", It.IsAny<MediaLinkResult>( ) ) )
            .Callback<string, MediaLinkResult>( ( _, _ ) => {
                if (System.Threading.Interlocked.Increment( ref attemptCount[0] ) >= 3) {
                    _ = allAttemptedTcs.TrySetResult( );
                }
            } )
            .ThrowsAsync( new InvalidOperationException( "Simulated failure" ) );
        _ = _cacheRepositoryMock
            .Setup( x => x.AddInputLinksAsync( It.Is<string>( s => s != "at://test/1" ), It.IsAny<MediaLinkResult>( ) ) )
            .Callback<string, MediaLinkResult>( ( _, _ ) => {
                if (System.Threading.Interlocked.Increment( ref attemptCount[0] ) >= 3) {
                    _ = allAttemptedTcs.TrySetResult( );
                }
            } )
            .Returns( Task.CompletedTask );

        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        await allAttemptedTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        // Assert - Should still attempt all 3 records
        _cacheRepositoryMock.Verify(
            x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ),
            Times.Exactly( 3 )
        );
    }

    /// <summary>
    /// Verifies that cancelling the host token lets the service stop without throwing, confirming
    /// cooperative shutdown.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WhenCancelled_ShouldStopGracefully( ) {
        // Arrange
        SetupEmptyRecordList( );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await cts.CancelAsync( );

        // Assert - Should complete without throwing
        await executeTask;
    }

    /// <summary>
    /// Verifies that the service calls
    /// <see cref="IATProtoStorageService.ListAllRecordsAsync"/> with the configured PDS URI and user DID
    /// from settings, so the bootstrap reads the intended repository.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_ShouldPassCorrectParametersToListAllRecords( ) {
        // Arrange: signal when ListAllRecordsAsync is called with the expected parameters
        TaskCompletionSource listCalledTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( s_testPdsUri, TestUserDid, It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Callback<Uri, string, CancellationToken, bool>( ( _, _, _, _ ) => listCalledTcs.TrySetResult( ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );

        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        await listCalledTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        // Assert - Verify correct PDS URI and DID were passed
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( s_testPdsUri, TestUserDid, It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ),
            Times.AtLeastOnce( )
        );
    }

    #endregion

    #region Settings Tests

    /// <summary>
    /// Verifies that <see cref="CacheBootstrapSettings"/> exposes the PDS URI, user DID, and bootstrap
    /// interval supplied to its constructor.
    /// </summary>
    [TestMethod]
    public void CacheBootstrapSettings_ShouldStoreValues( ) {
        // Arrange
        Uri pdsUri = new( "https://custom.pds.example" );
        string userDid = "did:plc:custom";
        TimeSpan interval = TimeSpan.FromHours( 12 );

        // Act
        CacheBootstrapSettings settings = new(
            pdsUri, userDid, interval,
            CacheDays: 30,
            RefreshInterval: TimeSpan.FromHours( 24 ),
            MaxRecordsPerRun: 100
        );

        // Assert
        Assert.AreEqual( pdsUri, settings.PdsUri );
        Assert.AreEqual( userDid, settings.UserDid );
        Assert.AreEqual( interval, settings.BootstrapInterval );
    }

    #endregion

    #region Helper Methods

    /// <summary>Builds the service under test from the configured mocks and settings.</summary>
    /// <returns>A new <see cref="CacheBootstrapBackgroundService"/> instance.</returns>
    private CacheBootstrapBackgroundService CreateService( ) {
        return new CacheBootstrapBackgroundService(
            _atProtoStorageMock.Object,
            _cacheRepositoryMock.Object,
            _redisMock.Object,
            _settings,
            _loggerMock.Object
        );
    }

    /// <summary>Configures the storage mock to return an empty record stream.</summary>
    private void SetupEmptyRecordList( ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );
    }

    /// <summary>Configures the storage mock to return the supplied records as an async stream.</summary>
    /// <param name="records">The records the bootstrap pass should enumerate.</param>
    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    /// <summary>
    /// Builds a test record pairing an AT-URI with a <see cref="MediaLinkResult"/> carrying a single
    /// Spotify provider result, for seeding the bootstrap stream.
    /// </summary>
    /// <param name="atUri">The AT-URI to associate with the record.</param>
    /// <returns>The AT-URI and its media-link result.</returns>
    private static (string AtUri, MediaLinkResult Result) CreateTestRecord( string atUri ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = DateTime.UtcNow
        };
        result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
            Artist = "Test Artist",
            Title = "Test Track",
            IsAlbum = false,
            ExternalId = "ISRC12345678",
            URL = "https://open.spotify.com/track/test"
        } );
        return (atUri, result);
    }

    #endregion

    #region Status Write Tests

    /// <summary>
    /// Verifies that when a bootstrap pass completes the status document written to Redis has
    /// <c>IsRunning</c> false and populated <c>LastRunTime</c>, <c>LastSuccessCount</c>, and
    /// <c>NextScheduledRun</c> fields.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunBootstrap_OnCompletion_WritesFullStatusWithAllFields( ) {
        // Use TCS to signal as soon as a completion write (IsRunning=false, LastRunTime≠null) lands.
        TaskCompletionSource completionTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        List<string> writtenJsons = [];

        _ = _redisDatabaseMock
            .Setup( d => d.StringSetAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<Expiration>( ),
                It.IsAny<StackExchange.Redis.ValueCondition>( ),
                It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisKey, RedisValue, Expiration, StackExchange.Redis.ValueCondition, CommandFlags>(
                ( _, v, _, _, _ ) => {
                    string json = v.ToString( );
                    lock (writtenJsons) { writtenJsons.Add( json ); }
                    try {
                        System.Text.Json.JsonDocument d2 = System.Text.Json.JsonDocument.Parse( json );
                        bool isRunning = d2.RootElement.GetProperty( "IsRunning" ).GetBoolean( );
                        bool hasLastRunTime = d2.RootElement.GetProperty( "LastRunTime" ).ValueKind != System.Text.Json.JsonValueKind.Null;
                        if (!isRunning && hasLastRunTime) {
                            _ = completionTcs.TrySetResult( );
                        }
                    } catch { }
                } )
            .ReturnsAsync( true );

        SetupRecordList( [CreateTestRecord( "at://test/1" )] );
        CacheBootstrapBackgroundService service = CreateService( );

        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        // Wait until the completion write arrives, then cancel
        await completionTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        Assert.IsNotEmpty( writtenJsons );

        string lastJson = writtenJsons[^1];
        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse( lastJson );
        System.Text.Json.JsonElement root = doc.RootElement;

        Assert.IsFalse( root.GetProperty( "IsRunning" ).GetBoolean( ) );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "LastRunTime" ).ValueKind );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "LastSuccessCount" ).ValueKind );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "NextScheduledRun" ).ValueKind );
    }

    /// <summary>
    /// Verifies that the status document written when a new pass starts (<c>IsRunning</c> true) carries
    /// over the previous completed run's fields (such as <c>LastSuccessCount</c> and
    /// <c>LastDurationSeconds</c>), so the in-progress status still reports the last known results.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunBootstrap_OnStart_PreservesPreviousCompletedRunFields( ) {
        CacheBootstrapStatus previousStatus = new( ) {
            IsRunning = false,
            LastRunTime = DateTimeOffset.UtcNow.AddHours( -6 ),
            LastSuccessCount = 500,
            LastErrorCount = 2,
            LastDurationSeconds = 120.5,
            RedisKeyCount = 1234567,
            NextScheduledRun = DateTimeOffset.UtcNow.AddHours( 6 )
        };
        string previousJson = System.Text.Json.JsonSerializer.Serialize( previousStatus );

        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( CacheBootstrapStatus.RedisKey, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)previousJson );

        // Signal when the first write (IsRunning=true) arrives
        TaskCompletionSource startTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        List<string> writtenJsons = [];
        _ = _redisDatabaseMock
            .Setup( d => d.StringSetAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<Expiration>( ),
                It.IsAny<StackExchange.Redis.ValueCondition>( ),
                It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisKey, RedisValue, Expiration, StackExchange.Redis.ValueCondition, CommandFlags>(
                ( _, v, _, _, _ ) => {
                    string json = v.ToString( );
                    writtenJsons.Add( json );
                    try {
                        System.Text.Json.JsonDocument d2 = System.Text.Json.JsonDocument.Parse( json );
                        if (d2.RootElement.GetProperty( "IsRunning" ).GetBoolean( )) {
                            _ = startTcs.TrySetResult( );
                        }
                    } catch { }
                } )
            .ReturnsAsync( true );

        SetupEmptyRecordList( );
        CacheBootstrapBackgroundService service = CreateService( );

        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        await startTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        Assert.IsNotEmpty( writtenJsons );
        string startJson = writtenJsons[0];
        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse( startJson );
        System.Text.Json.JsonElement root = doc.RootElement;

        Assert.IsTrue( root.GetProperty( "IsRunning" ).GetBoolean( ) );
        Assert.AreEqual( 500, root.GetProperty( "LastSuccessCount" ).GetInt32( ) );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "LastDurationSeconds" ).ValueKind );
    }

    /// <summary>
    /// Verifies that when the record stream throws mid-pass, the final status document still reports
    /// <c>IsRunning</c> false and preserves the prior completed run's fields (success count, last-run
    /// time, duration, next scheduled run), so a failed pass does not erase the last good status.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RunBootstrap_WhenListAllRecordsThrows_PreservesPriorCompletedRunFields( ) {
        CacheBootstrapStatus priorRun = new( ) {
            IsRunning = false,
            LastRunTime = DateTimeOffset.UtcNow.AddHours( -6 ),
            LastSuccessCount = 247404,
            LastErrorCount = 0,
            LastDurationSeconds = 4565.9,
            RedisKeyCount = 2882803,
            NextScheduledRun = DateTimeOffset.UtcNow.AddHours( 6 )
        };
        string priorJson = System.Text.Json.JsonSerializer.Serialize( priorRun );

        _ = _redisDatabaseMock
            .Setup( d => d.StringGetAsync( CacheBootstrapStatus.RedisKey, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)priorJson );

        // Signal when the error-path completion write (IsRunning=false, NextScheduledRun present) lands
        TaskCompletionSource errorCompletionTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        List<string> writtenJsons = [];
        _ = _redisDatabaseMock
            .Setup( d => d.StringSetAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<Expiration>( ),
                It.IsAny<StackExchange.Redis.ValueCondition>( ),
                It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisKey, RedisValue, Expiration, StackExchange.Redis.ValueCondition, CommandFlags>(
                ( _, v, _, _, _ ) => {
                    string json = v.ToString( );
                    writtenJsons.Add( json );
                    try {
                        System.Text.Json.JsonDocument d2 = System.Text.Json.JsonDocument.Parse( json );
                        bool isRunning = d2.RootElement.GetProperty( "IsRunning" ).GetBoolean( );
                        bool hasNextScheduled = d2.RootElement.GetProperty( "NextScheduledRun" ).ValueKind != System.Text.Json.JsonValueKind.Null;
                        if (!isRunning && hasNextScheduled) {
                            _ = errorCompletionTcs.TrySetResult( );
                        }
                    } catch { }
                } )
            .ReturnsAsync( true );

        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Throws( new InvalidOperationException( "Simulated CAR parse failure" ) );

        CacheBootstrapBackgroundService service = CreateService( );

        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );

        await errorCompletionTcs.Task.WaitAsync( TestContext.CancellationToken );
        await cts.CancelAsync( );
        await executeTask;

        Assert.IsNotEmpty( writtenJsons );

        string lastJson = writtenJsons[^1];
        System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse( lastJson );
        System.Text.Json.JsonElement root = doc.RootElement;

        Assert.IsFalse( root.GetProperty( "IsRunning" ).GetBoolean( ) );
        Assert.AreEqual( 247404, root.GetProperty( "LastSuccessCount" ).GetInt32( ) );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "LastRunTime" ).ValueKind );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "LastDurationSeconds" ).ValueKind );
        Assert.AreNotEqual( System.Text.Json.JsonValueKind.Null, root.GetProperty( "NextScheduledRun" ).ValueKind );
    }

    #endregion
}
