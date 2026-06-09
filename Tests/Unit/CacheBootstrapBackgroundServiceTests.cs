using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.CacheBootstrap;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CacheBootstrapBackgroundService"/> to verify bootstrap execution,
/// periodic scheduling, and error handling.
/// </summary>
[TestClass]
public class CacheBootstrapBackgroundServiceTests {

    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<IMediaLinkCacheRepository> _cacheRepositoryMock = null!;
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _redisDatabaseMock = null!;
    private Mock<ILogger<CacheBootstrapBackgroundService>> _loggerMock = null!;
    private CacheBootstrapSettings _settings = null!;

    /// <summary>
    /// Gets or sets the test context for the current test.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    private const string TestUserDid = "did:plc:testuser123";

    /// <summary>
    /// Initializes test dependencies before each test method.
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
            TimeSpan.FromHours( 6 )
        );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor creates a valid instance when provided with valid dependencies.
    /// </summary>
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
    /// Verifies that <see cref="CacheBootstrapBackgroundService"/> bootstraps the cache by loading
    /// records from ATProto storage and populating the cache repository on startup.
    /// Converted from Task.Delay(100) to TCS-signaling to eliminate flake risk on slow CI.
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
    /// Verifies that <see cref="CacheBootstrapBackgroundService"/> completes successfully when
    /// the ATProto storage contains no records to bootstrap.
    /// Converted from Task.Delay(100) to TCS-signaling to eliminate flake risk on slow CI.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WithEmptyCollection_ShouldCompleteWithoutErrors( ) {
        // Arrange: signal when ListAllRecordsAsync is called (first run completed when it returns)
        TaskCompletionSource listCalledTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Callback<Uri, string, CancellationToken>( ( _, _, _ ) => listCalledTcs.TrySetResult( ) )
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
    /// Verifies that <see cref="CacheBootstrapBackgroundService"/> continues processing remaining
    /// records when individual record caching fails, ensuring fault tolerance.
    /// Converted from Task.Delay(100) to TCS-signaling to eliminate flake risk on slow CI.
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
    /// Verifies that <see cref="CacheBootstrapBackgroundService"/> stops gracefully when
    /// the cancellation token is cancelled.
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
    /// Verifies that <see cref="CacheBootstrapBackgroundService"/> passes the correct PDS URI
    /// and user DID from settings when listing records from ATProto storage.
    /// Converted from Task.Delay(100) to TCS-signaling to eliminate flake risk on slow CI.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_ShouldPassCorrectParametersToListAllRecords( ) {
        // Arrange: signal when ListAllRecordsAsync is called with the expected parameters
        TaskCompletionSource listCalledTcs = new( TaskCreationOptions.RunContinuationsAsynchronously );
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( s_testPdsUri, TestUserDid, It.IsAny<CancellationToken>( ) ) )
            .Callback<Uri, string, CancellationToken>( ( _, _, _ ) => listCalledTcs.TrySetResult( ) )
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
            x => x.ListAllRecordsAsync( s_testPdsUri, TestUserDid, It.IsAny<CancellationToken>( ) ),
            Times.AtLeastOnce( )
        );
    }

    #endregion

    #region Settings Tests

    /// <summary>
    /// Verifies that <see cref="CacheBootstrapSettings"/> correctly stores all configuration values
    /// including PDS URI, user DID, and bootstrap interval.
    /// </summary>
    [TestMethod]
    public void CacheBootstrapSettings_ShouldStoreValues( ) {
        // Arrange
        Uri pdsUri = new( "https://custom.pds.example" );
        string userDid = "did:plc:custom";
        TimeSpan interval = TimeSpan.FromHours( 12 );

        // Act
        CacheBootstrapSettings settings = new( pdsUri, userDid, interval );

        // Assert
        Assert.AreEqual( pdsUri, settings.PdsUri );
        Assert.AreEqual( userDid, settings.UserDid );
        Assert.AreEqual( interval, settings.BootstrapInterval );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a <see cref="CacheBootstrapBackgroundService"/> instance with the mocked dependencies.
    /// </summary>
    /// <returns>A configured <see cref="CacheBootstrapBackgroundService"/> instance.</returns>
    private CacheBootstrapBackgroundService CreateService( ) {
        return new CacheBootstrapBackgroundService(
            _atProtoStorageMock.Object,
            _cacheRepositoryMock.Object,
            _redisMock.Object,
            _settings,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Configures the ATProto storage mock to return an empty list of records.
    /// </summary>
    private void SetupEmptyRecordList( ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );
    }

    /// <summary>
    /// Configures the ATProto storage mock to return the specified list of records.
    /// </summary>
    /// <param name="records">The records to return from the mock.</param>
    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    /// <summary>
    /// Creates a test record with the specified AT URI for testing.
    /// </summary>
    /// <param name="atUri">The ATProto URI for the record.</param>
    /// <returns>A tuple containing the AT URI and the <see cref="MediaLinkResult"/>.</returns>
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
    /// Verifies that a completed run writes a full status object with all fields populated.
    /// Uses a deterministic TaskCompletionSource rather than a fixed-time delay to avoid
    /// flaky behavior on slow CI.
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
            .Callback( ( RedisKey _, RedisValue v, Expiration _, StackExchange.Redis.ValueCondition _, CommandFlags _ ) => {
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
    /// Verifies that the start-of-run status write preserves completed-run fields
    /// from a previous run rather than clearing them.
    /// Uses a deterministic TaskCompletionSource to avoid flaky fixed-time delays.
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
            .Callback( ( RedisKey _, RedisValue v, Expiration _, StackExchange.Redis.ValueCondition _, CommandFlags _ ) => {
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
    /// Verifies that a fatal error during ListAllRecordsAsync preserves the prior completed-run
    /// fields in Redis rather than clobbering them with zero counts.
    /// Uses a deterministic TaskCompletionSource to avoid flaky fixed-time delays.
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
            .Callback( ( RedisKey _, RedisValue v, Expiration _, StackExchange.Redis.ValueCondition _, CommandFlags _ ) => {
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
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
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
