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
        _loggerMock = new Mock<ILogger<CacheBootstrapBackgroundService>>( );

        // Setup Redis mock to return empty endpoints (avoids null reference in diagnostics)
        _ = _redisMock.Setup( r => r.GetEndPoints( It.IsAny<bool>( ) ) ).Returns( [] );

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
        SetupRecordList( records );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act - Start and immediately cancel
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken ); // Give time for bootstrap to complete
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
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_WithEmptyCollection_ShouldCompleteWithoutErrors( ) {
        // Arrange
        SetupEmptyRecordList( );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
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

        // Setup first record to fail
        _ = _cacheRepositoryMock
            .Setup( x => x.AddInputLinksAsync( "at://test/1", It.IsAny<MediaLinkResult>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Simulated failure" ) );
        _ = _cacheRepositoryMock
            .Setup( x => x.AddInputLinksAsync( It.Is<string>( s => s != "at://test/1" ), It.IsAny<MediaLinkResult>( ) ) )
            .Returns( Task.CompletedTask );

        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
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
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ExecuteAsync_ShouldPassCorrectParametersToListAllRecords( ) {
        // Arrange
        SetupEmptyRecordList( );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await Task.Delay( 100, TestContext.CancellationToken );
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
}
