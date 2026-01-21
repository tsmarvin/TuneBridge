using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.Services.LinkResolver;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="LookupOrchestrator"/> to verify proper orchestration
/// of lookup operations including cache checking, deduplication, saga creation, and queue submission.
/// </summary>
[TestClass]
public class LookupOrchestratorTests {
    private Mock<IMediaLinkCacheRepository> _cacheMock = null!;
    private Mock<IRequestDeduplicator> _deduplicatorMock = null!;
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    private Mock<IProviderQueueResolver<QueuedLookupRequest>> _queueResolverMock = null!;
    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<IRequestQueue<QueuedLookupRequest>> _queueMock = null!;
    private Mock<ILogger<LookupOrchestrator>> _loggerMock = null!;
    private HashSet<SupportedProviders> _enabledProviders = null!;
    private LookupOrchestrator _orchestrator = null!;

    private const string TestIsrc = "USRC17607839";
    private const string TestUpc = "012345678901";
    private const string TestTitle = "Test Song";
    private const string TestArtist = "Test Artist";
    private const string TestRecordUri = "at://did:plc:test/com.bridgebeats.media.link/123abc";

    [TestInitialize]
    public void Initialize( ) {
        _cacheMock = new Mock<IMediaLinkCacheRepository>( );
        _deduplicatorMock = new Mock<IRequestDeduplicator>( );
        _sagaManagerMock = new Mock<ISagaStateManager>( );
        _queueResolverMock = new Mock<IProviderQueueResolver<QueuedLookupRequest>>( );
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _queueMock = new Mock<IRequestQueue<QueuedLookupRequest>>( );
        _loggerMock = new Mock<ILogger<LookupOrchestrator>>( );

        _enabledProviders = [SupportedProviders.Spotify, SupportedProviders.AppleMusic, SupportedProviders.Tidal];

        // Default queue resolver setup
        _ = _queueResolverMock
            .Setup( r => r.GetQueue( It.IsAny<SupportedProviders>( ) ) )
            .Returns( _queueMock.Object );

        _orchestrator = CreateOrchestrator( );
    }

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        LookupOrchestrator orchestrator = CreateOrchestrator( );

        // Assert
        Assert.IsNotNull( orchestrator );
    }

    [TestMethod]
    public void Constructor_WithNullCache_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                null!,
                _deduplicatorMock.Object,
                _sagaManagerMock.Object,
                _queueResolverMock.Object,
                _atProtoStorageMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    [TestMethod]
    public void Constructor_WithNullDeduplicator_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                _cacheMock.Object,
                null!,
                _sagaManagerMock.Object,
                _queueResolverMock.Object,
                _atProtoStorageMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    [TestMethod]
    public void Constructor_WithNullSagaManager_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                _cacheMock.Object,
                _deduplicatorMock.Object,
                null!,
                _queueResolverMock.Object,
                _atProtoStorageMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    [TestMethod]
    public void Constructor_WithNullQueueResolver_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                _cacheMock.Object,
                _deduplicatorMock.Object,
                _sagaManagerMock.Object,
                null!,
                _atProtoStorageMock.Object,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    [TestMethod]
    public void Constructor_WithNullAtProtoStorage_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                _cacheMock.Object,
                _deduplicatorMock.Object,
                _sagaManagerMock.Object,
                _queueResolverMock.Object,
                null!,
                _enabledProviders,
                _loggerMock.Object
            )
        );
    }

    [TestMethod]
    public void Constructor_WithNullEnabledProviders_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                _cacheMock.Object,
                _deduplicatorMock.Object,
                _sagaManagerMock.Object,
                _queueResolverMock.Object,
                _atProtoStorageMock.Object,
                null!,
                _loggerMock.Object
            )
        );
    }

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new LookupOrchestrator(
                _cacheMock.Object,
                _deduplicatorMock.Object,
                _sagaManagerMock.Object,
                _queueResolverMock.Object,
                _atProtoStorageMock.Object,
                _enabledProviders,
                null!
            )
        );
    }

    #endregion

    #region LookupByIsrcAsync Tests

    [TestMethod]
    public async Task LookupByIsrcAsync_WithNullIsrc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( null! );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_WithEmptyIsrc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( string.Empty );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_WithWhitespaceIsrc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( "   " );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_WithCacheHit_ShouldReturnCachedResult( ) {
        // Arrange
        MediaLinkResult cachedResult = CreateMediaLinkResult( );
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( (cachedResult, TestRecordUri, false) );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsFalse( result.IsPartial );
        _deduplicatorMock.Verify(
            d => d.TryAcquireAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ),
            Times.Never
        );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_WithCacheMissAndDeduplicationAcquired_ShouldCreateSagaAndEnqueue( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult result1 = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( result1 );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        _sagaManagerMock.Verify( s => s.GetOrCreateAsync(
            It.IsAny<string>( ),
            It.Is<string>( k => k.Contains( "IsrcLookup" ) ),
            LookupRequestType.IsrcLookup,
            It.Is<string>( v => v.ToUpperInvariant( ) == TestIsrc.ToUpperInvariant( ) )
        ), Times.Once );
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), QueuePriority.Interactive ),
            Times.Once
        );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_WithInFlightRequest_ShouldWaitForCompletion( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationInFlight( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( TestRecordUri );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( TestRecordUri ) )
            .ReturnsAsync( expectedResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsFalse( result.IsPartial );
        _sagaManagerMock.Verify(
            s => s.GetOrCreateAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ), It.IsAny<string>( ) ),
            Times.Never
        );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_WithInFlightRequestAndTimeout_ShouldRetryCache( ) {
        // Arrange
        MediaLinkResult cachedResult = CreateMediaLinkResult( );

        _ = _cacheMock
            .SetupSequence( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null )
            .ReturnsAsync( (cachedResult, TestRecordUri, false) );

        SetupDeduplicationInFlight( );
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( (string?)null ); // Timeout

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        _cacheMock.Verify( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ), Times.Exactly( 2 ) );
    }

    #endregion

    #region LookupByUpcAsync Tests

    [TestMethod]
    public async Task LookupByUpcAsync_WithNullUpc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByUpcAsync( null! );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    [TestMethod]
    public async Task LookupByUpcAsync_WithCacheHit_ShouldReturnCachedResult( ) {
        // Arrange
        MediaLinkResult cachedResult = CreateMediaLinkResult( );
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByUPCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( (cachedResult, TestRecordUri, false) );

        // Act
        LookupResult result = await _orchestrator.LookupByUpcAsync( TestUpc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    [TestMethod]
    public async Task LookupByUpcAsync_WithCacheMiss_ShouldQueueWithIsAlbumTrue( ) {
        // Arrange
        SetupCacheMissForUpc( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        _ = await _orchestrator.LookupByUpcAsync( TestUpc );

        // Assert
        _queueMock.Verify( q => q.EnqueueAsync(
            It.Is<QueuedLookupRequest>( r => r.IsAlbum == true && r.LookupType == LookupRequestType.UpcLookup ),
            QueuePriority.Interactive
        ), Times.Once );
    }

    #endregion

    #region LookupByMetadataAsync Tests

    [TestMethod]
    public async Task LookupByMetadataAsync_WithNullTitle_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByMetadataAsync( null!, TestArtist );

        // Assert
        Assert.IsNull( result.Result );
    }

    [TestMethod]
    public async Task LookupByMetadataAsync_WithNullArtist_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByMetadataAsync( TestTitle, null! );

        // Assert
        Assert.IsNull( result.Result );
    }

    [TestMethod]
    public async Task LookupByMetadataAsync_WithCacheHit_ShouldReturnCachedResult( ) {
        // Arrange
        MediaLinkResult cachedResult = CreateMediaLinkResult( );
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByMetadataAsync( TestTitle, TestArtist ) )
            .ReturnsAsync( (cachedResult, TestRecordUri, false) );

        // Act
        LookupResult result = await _orchestrator.LookupByMetadataAsync( TestTitle, TestArtist );

        // Assert
        Assert.IsNotNull( result.Result );
    }

    [TestMethod]
    public async Task LookupByMetadataAsync_WithCacheMiss_ShouldQueueWithTitleAndArtist( ) {
        // Arrange
        SetupCacheMissForMetadata( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        _ = await _orchestrator.LookupByMetadataAsync( TestTitle, TestArtist );

        // Assert
        _queueMock.Verify( q => q.EnqueueAsync(
            It.Is<QueuedLookupRequest>( r =>
                r.Title == TestTitle &&
                r.Artist == TestArtist &&
                r.LookupType == LookupRequestType.SongLookup
            ),
            QueuePriority.Interactive
        ), Times.Once );
    }

    #endregion

    #region LookupByProviderIdAsync Tests

    [TestMethod]
    public async Task LookupByProviderIdAsync_WithNullProviderId_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByProviderIdAsync( null!, SupportedProviders.Spotify, false );

        // Assert
        Assert.IsNull( result.Result );
    }

    [TestMethod]
    public async Task LookupByProviderIdAsync_ForTrack_ShouldUseSongIdLookupType( ) {
        // Arrange
        SetupCacheMissForProviderId( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        _ = await _orchestrator.LookupByProviderIdAsync( "spotify-id", SupportedProviders.Spotify, false );

        // Assert
        _queueMock.Verify( q => q.EnqueueAsync(
            It.Is<QueuedLookupRequest>( r => r.LookupType == LookupRequestType.SongIdLookup && r.IsAlbum == false ),
            QueuePriority.Interactive
        ), Times.Once );
    }

    [TestMethod]
    public async Task LookupByProviderIdAsync_ForAlbum_ShouldUseAlbumIdLookupType( ) {
        // Arrange
        SetupCacheMissForProviderId( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        _ = await _orchestrator.LookupByProviderIdAsync( "spotify-id", SupportedProviders.Spotify, true );

        // Assert
        _queueMock.Verify( q => q.EnqueueAsync(
            It.Is<QueuedLookupRequest>( r => r.LookupType == LookupRequestType.AlbumIdLookup && r.IsAlbum == true ),
            QueuePriority.Interactive
        ), Times.Once );
    }

    #endregion

    #region LookupByContentAsync Tests

    [TestMethod]
    public async Task LookupByContentAsync_WithNullContent_ShouldReturnEmpty( ) {
        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( null! )) {
            results.Add( result );
        }

        // Assert
        Assert.IsEmpty( results );
    }

    [TestMethod]
    public async Task LookupByContentAsync_WithEmptyContent_ShouldReturnEmpty( ) {
        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( string.Empty )) {
            results.Add( result );
        }

        // Assert
        Assert.IsEmpty( results );
    }

    [TestMethod]
    public async Task LookupByContentAsync_WithSpotifyUrl_ShouldReturnResult( ) {
        // Arrange
        string content = "Check out this song: https://open.spotify.com/track/abc123";
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( content )) {
            results.Add( result );
        }

        // Assert
        Assert.HasCount( 1, results );
        Assert.IsNotNull( results[0].Result );
    }

    [TestMethod]
    public async Task LookupByContentAsync_WithAppleMusicUrl_ShouldReturnResult( ) {
        // Arrange
        string content = "Check out: https://music.apple.com/us/album/test/12345";
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( content )) {
            results.Add( result );
        }

        // Assert
        Assert.HasCount( 1, results );
    }

    [TestMethod]
    public async Task LookupByContentAsync_WithTidalUrl_ShouldReturnResult( ) {
        // Arrange
        string content = "Listen here: https://tidal.com/browse/track/12345";
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( content )) {
            results.Add( result );
        }

        // Assert
        Assert.HasCount( 1, results );
    }

    [TestMethod]
    public async Task LookupByContentAsync_WithMultipleUrls_ShouldDeduplicateSameLinks( ) {
        // Arrange
        string url = "https://open.spotify.com/track/abc123";
        string content = $"Check these: {url} and again {url}";
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );
        SetupDeduplicationWaitWithResult( );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( content )) {
            results.Add( result );
        }

        // Assert - should only process the unique URL once
        Assert.HasCount( 1, results );
    }

    [TestMethod]
    public async Task LookupByContentAsync_WithNoMusicUrls_ShouldReturnEmpty( ) {
        // Arrange
        string content = "Check out https://example.com and https://github.com";

        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( content )) {
            results.Add( result );
        }

        // Assert
        Assert.IsEmpty( results );
    }

    #endregion

    #region Saga and Partial Result Tests

    [TestMethod]
    public async Task LookupByIsrcAsync_WhenSagaReturnsPartialResult_ShouldReturnPartialWithInfo( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( TestRecordUri );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( new LookupSagaState {
                SagaId = "test-saga",
                LookupKey = "test-key",
                LookupType = LookupRequestType.IsrcLookup,
                LookupValue = TestIsrc,
                CreatedAt = DateTimeOffset.UtcNow,
                IsPartial = true,
                PartialResultUri = TestRecordUri,
                ProviderStates = [],
                RateLimitInfo = rateLimitInfo
            } );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        Assert.HasCount( 1, result.RateLimitedProviders );
        Assert.AreEqual( SupportedProviders.AppleMusic, result.RateLimitedProviders[0].Provider );
    }

    [TestMethod]
    public async Task LookupByIsrcAsync_OnError_ShouldReleaseDeduplicationLock( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );

        _ = _sagaManagerMock
            .Setup( s => s.GetOrCreateAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ), It.IsAny<string>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );

        // Act & Assert
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>( ( ) =>
            _orchestrator.LookupByIsrcAsync( TestIsrc )
        );

        _deduplicatorMock.Verify( d => d.ReleaseAsync( It.IsAny<string>( ), null ), Times.Once );
    }

    #endregion

    #region Helper Methods

    private LookupOrchestrator CreateOrchestrator( ) {
        return new LookupOrchestrator(
            _cacheMock.Object,
            _deduplicatorMock.Object,
            _sagaManagerMock.Object,
            _queueResolverMock.Object,
            _atProtoStorageMock.Object,
            _enabledProviders,
            _loggerMock.Object
        );
    }

    private static MediaLinkResult CreateMediaLinkResult( ) {
        return new MediaLinkResult {
            Results = new Dictionary<SupportedProviders, MusicLookupResult> {
                [SupportedProviders.Spotify] = new MusicLookupResult {
                    Artist = TestArtist,
                    Title = TestTitle,
                    ExternalId = TestIsrc,
                    URL = "https://open.spotify.com/track/123",
                    ArtUrl = "https://example.com/art.jpg",
                    IsAlbum = false
                }
            }
        };
    }

    private void SetupCacheMiss( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    private void SetupCacheMissForUpc( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByUPCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    private void SetupCacheMissForMetadata( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByMetadataAsync( It.IsAny<string>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    private void SetupCacheMissForProviderId( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByProviderIdAsync( It.IsAny<string>( ), It.IsAny<SupportedProviders>( ), It.IsAny<bool>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    private void SetupCacheMissForUrl( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    private void SetupDeduplicationAcquired( ) {
        _ = _deduplicatorMock
            .Setup( d => d.TryAcquireAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( new DeduplicationResult( Acquired: true, AlreadyInFlight: false, RequestKey: "test-key" ) );
    }

    private void SetupDeduplicationInFlight( ) {
        _ = _deduplicatorMock
            .Setup( d => d.TryAcquireAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( new DeduplicationResult( Acquired: false, AlreadyInFlight: true, RequestKey: "test-key" ) );
    }

    private void SetupSagaCreation( ) {
        _ = _sagaManagerMock
            .Setup( s => s.GetOrCreateAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( new LookupSagaState {
                SagaId = "test-saga",
                LookupKey = "test-key",
                LookupType = LookupRequestType.IsrcLookup,
                LookupValue = TestIsrc,
                CreatedAt = DateTimeOffset.UtcNow,
                ProviderStates = []
            } );

        _ = _sagaManagerMock
            .Setup( s => s.InitializeProviderStatesAsync( It.IsAny<string>( ), It.IsAny<HashSet<SupportedProviders>>( ) ) )
            .Returns( Task.CompletedTask );

        _ = _sagaManagerMock
            .Setup( s => s.SetInitialProviderAsync( It.IsAny<string>( ), It.IsAny<SupportedProviders>( ) ) )
            .Returns( Task.CompletedTask );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( new LookupSagaState {
                SagaId = "test-saga",
                LookupKey = "test-key",
                LookupType = LookupRequestType.IsrcLookup,
                LookupValue = TestIsrc,
                CreatedAt = DateTimeOffset.UtcNow,
                IsPartial = false,
                ProviderStates = []
            } );
    }

    private void SetupDeduplicationWaitWithResult( ) {
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( TestRecordUri );
    }

    #endregion
}
