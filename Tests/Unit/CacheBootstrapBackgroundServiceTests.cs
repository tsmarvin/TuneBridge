using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.CacheBootstrap;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CacheBootstrapBackgroundService"/> to verify bootstrap execution,
/// periodic scheduling, and error handling.
/// </summary>
[TestClass]
public class CacheBootstrapBackgroundServiceTests {

    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<IMediaLinkCacheRepository> _cacheRepositoryMock = null!;
    private Mock<ILogger<CacheBootstrapBackgroundService>> _loggerMock = null!;
    private CacheBootstrapSettings _settings = null!;

    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    private const string TestUserDid = "did:plc:testuser123";

    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _cacheRepositoryMock = new Mock<IMediaLinkCacheRepository>( );
        _loggerMock = new Mock<ILogger<CacheBootstrapBackgroundService>>( );
        _settings = new CacheBootstrapSettings(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 6 )
        );
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        CacheBootstrapBackgroundService service = CreateService( );

        // Assert
        Assert.IsNotNull( service );
    }

    #endregion

    #region ExecuteAsync Tests

    [TestMethod]
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
        await Task.Delay( 100 ); // Give time for bootstrap to complete
        await cts.CancelAsync( );
        await executeTask;

        // Assert - AddInputLinksAsync should be called for each record
        _cacheRepositoryMock.Verify(
            x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ),
            Times.Exactly( 3 )
        );
    }

    [TestMethod]
    public async Task ExecuteAsync_WithEmptyCollection_ShouldCompleteWithoutErrors( ) {
        // Arrange
        SetupEmptyRecordList( );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await Task.Delay( 100 );
        await cts.CancelAsync( );
        await executeTask;

        // Assert - No exceptions thrown, AddInputLinksAsync never called
        _cacheRepositoryMock.Verify(
            x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ),
            Times.Never( )
        );
    }

    [TestMethod]
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
        await Task.Delay( 100 );
        await cts.CancelAsync( );
        await executeTask;

        // Assert - Should still attempt all 3 records
        _cacheRepositoryMock.Verify(
            x => x.AddInputLinksAsync( It.IsAny<string>( ), It.IsAny<MediaLinkResult>( ) ),
            Times.Exactly( 3 )
        );
    }

    [TestMethod]
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

    [TestMethod]
    public async Task ExecuteAsync_ShouldPassCorrectParametersToListAllRecords( ) {
        // Arrange
        SetupEmptyRecordList( );
        CacheBootstrapBackgroundService service = CreateService( );

        // Act
        using CancellationTokenSource cts = new( );
        Task executeTask = service.StartAsync( cts.Token );
        await Task.Delay( 100 );
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

    private CacheBootstrapBackgroundService CreateService( ) {
        return new CacheBootstrapBackgroundService(
            _atProtoStorageMock.Object,
            _cacheRepositoryMock.Object,
            _settings,
            _loggerMock.Object
        );
    }

    private void SetupEmptyRecordList( ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );
    }

    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

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
