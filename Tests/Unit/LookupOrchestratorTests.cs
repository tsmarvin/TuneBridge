using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Services.LinkResolver;
using Microsoft.Extensions.Logging;
using Moq;

#pragma warning disable MSTEST0049 // ILookupOrchestrator methods do not have CancellationToken overloads

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

    /// <summary>
    /// Gets or sets the test context for the current test.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Initializes mocks and test dependencies before each test.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor creates a valid instance with all valid dependencies.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        LookupOrchestrator orchestrator = CreateOrchestrator( );

        // Assert
        Assert.IsNotNull( orchestrator );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when cache is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when deduplicator is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when saga manager is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when queue resolver is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when ATProto storage is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when enabled providers is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when logger is null.
    /// </summary>
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

    /// <summary>
    /// Verifies that LookupByIsrcAsync returns an empty result when ISRC is null.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithNullIsrc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( null! );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    /// <summary>
    /// Verifies that LookupByIsrcAsync returns an empty result when ISRC is empty.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithEmptyIsrc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( string.Empty );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    /// <summary>
    /// Verifies that LookupByIsrcAsync returns an empty result when ISRC contains only whitespace.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithWhitespaceIsrc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( "   " );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    /// <summary>
    /// Verifies that LookupByIsrcAsync returns cached result when cache hit occurs.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByIsrcAsync creates a saga and enqueues request when cache misses and deduplication is acquired.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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
            It.Is<string>( v => v.Equals( TestIsrc, StringComparison.InvariantCultureIgnoreCase ) )
        ), Times.Once );
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), QueuePriority.Interactive ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that the queued request carries the Interactive origin priority so the saga
    /// coordinator can enqueue secondary lookups at the originating caller's priority.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithCacheMissAndDeduplicationAcquired_ShouldEnqueueWithInteractiveOriginPriority( ) {
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
        _ = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        _queueMock.Verify(
            q => q.EnqueueAsync(
                It.Is<QueuedLookupRequest>( r => r.OriginPriority == QueuePriority.Interactive ),
                QueuePriority.Interactive
            ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that LookupByIsrcAsync waits for completion when request is already in-flight.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByIsrcAsync retries cache check when in-flight request times out.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByUpcAsync returns an empty result when UPC is null.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByUpcAsync_WithNullUpc_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByUpcAsync( null! );

        // Assert
        Assert.IsNull( result.Result );
        Assert.IsFalse( result.IsPartial );
    }

    /// <summary>
    /// Verifies that LookupByUpcAsync returns cached result when cache hit occurs.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByUpcAsync queues the request with IsAlbum set to true when cache misses.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByMetadataAsync returns an empty result when title is null.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByMetadataAsync_WithNullTitle_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByMetadataAsync( null!, TestArtist );

        // Assert
        Assert.IsNull( result.Result );
    }

    /// <summary>
    /// Verifies that LookupByMetadataAsync returns an empty result when artist is null.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByMetadataAsync_WithNullArtist_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByMetadataAsync( TestTitle, null! );

        // Assert
        Assert.IsNull( result.Result );
    }

    /// <summary>
    /// Verifies that LookupByMetadataAsync returns cached result when cache hit occurs.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByMetadataAsync queues the request with title and artist metadata when cache misses.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByProviderIdAsync returns an empty result when provider ID is null.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByProviderIdAsync_WithNullProviderId_ShouldReturnEmptyResult( ) {
        // Act
        LookupResult result = await _orchestrator.LookupByProviderIdAsync( null!, SupportedProviders.Spotify, false );

        // Assert
        Assert.IsNull( result.Result );
    }

    /// <summary>
    /// Verifies that LookupByProviderIdAsync uses SongIdLookup type for track lookups.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByProviderIdAsync uses AlbumIdLookup type for album lookups.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByContentAsync returns an empty collection when content is null.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByContentAsync_WithNullContent_ShouldReturnEmpty( ) {
        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( null! )) {
            results.Add( result );
        }

        // Assert
        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that LookupByContentAsync returns an empty collection when content is empty.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByContentAsync_WithEmptyContent_ShouldReturnEmpty( ) {
        // Act
        List<LookupResult> results = [];
        await foreach (LookupResult result in _orchestrator.LookupByContentAsync( string.Empty )) {
            results.Add( result );
        }

        // Assert
        Assert.IsEmpty( results );
    }

    /// <summary>
    /// Verifies that LookupByContentAsync returns a result when content contains a Spotify URL.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByContentAsync returns a result when content contains an Apple Music URL.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByContentAsync returns a result when content contains a Tidal URL.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByContentAsync deduplicates identical URLs in the content.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByContentAsync uses the full interactive wait budget when the
    /// content contains a single link.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByContentAsync_WithSingleLink_ShouldUseFullInteractiveBudget( ) {
        // Arrange
        string content = "Check out https://open.spotify.com/track/abc123";
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        List<TimeSpan> capturedTimeouts = [];
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .Callback<string, TimeSpan, CancellationToken>( ( _, timeout, _ ) => capturedTimeouts.Add( timeout ) )
            .ReturnsAsync( TestRecordUri );

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
        Assert.HasCount( 1, capturedTimeouts );
        Assert.AreEqual( TimeSpan.FromSeconds( 30 ), capturedTimeouts[0] );
    }

    /// <summary>
    /// Verifies that LookupByContentAsync scales the per-link wait budget down when multiple
    /// links are present so the total stays under the content wait budget (90s).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByContentAsync_WithMultipleLinks_ShouldScalePerLinkWaitBudget( ) {
        // Arrange - 4 links: per-link budget = min(30s, 90s / 4) = 22.5s
        string content = string.Join(
            " ",
            Enumerable.Range( 1, 4 ).Select( i => $"https://open.spotify.com/track/track{i}" )
        );
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        List<TimeSpan> capturedTimeouts = [];
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .Callback<string, TimeSpan, CancellationToken>( ( _, timeout, _ ) => capturedTimeouts.Add( timeout ) )
            .ReturnsAsync( TestRecordUri );

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
        Assert.HasCount( 4, results );
        Assert.HasCount( 4, capturedTimeouts );
        foreach (TimeSpan timeout in capturedTimeouts) {
            Assert.AreEqual( TimeSpan.FromSeconds( 22.5 ), timeout );
        }
    }

    /// <summary>
    /// Verifies that LookupByContentAsync applies the per-link wait budget floor (5s) when
    /// the content contains many links.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByContentAsync_WithManyLinks_ShouldApplyPerLinkBudgetFloor( ) {
        // Arrange - 20 links: 90s / 20 = 4.5s, clamped up to the 5s floor
        string content = string.Join(
            " ",
            Enumerable.Range( 1, 20 ).Select( i => $"https://open.spotify.com/track/track{i}" )
        );
        SetupCacheMissForUrl( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        List<TimeSpan> capturedTimeouts = [];
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .Callback<string, TimeSpan, CancellationToken>( ( _, timeout, _ ) => capturedTimeouts.Add( timeout ) )
            .ReturnsAsync( TestRecordUri );

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
        Assert.HasCount( 20, results );
        Assert.HasCount( 20, capturedTimeouts );
        foreach (TimeSpan timeout in capturedTimeouts) {
            Assert.AreEqual( TimeSpan.FromSeconds( 5 ), timeout );
        }
    }

    /// <summary>
    /// Verifies that LookupByContentAsync returns an empty collection when content has no music URLs.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that LookupByIsrcAsync returns a partial result with rate limit info when saga indicates partial completion.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Verifies that the orchestrator keeps waiting after a partial result and returns the final
    /// result when it is published within the time budget.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenPartialThenFinalWithinBudget_ShouldReturnFinal( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        // First read: saga is partial with a pending provider; second read: saga finalized
        LookupSagaState partialSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );
        LookupSagaState finalSaga = CreateSagaState(
            isPartial: false,
            partialResultUri: partialUri,
            finalResultUri: TestRecordUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, true)]
        );

        _ = _sagaManagerMock
            .SetupSequence( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialSaga )
            .ReturnsAsync( finalSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( TestRecordUri );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsFalse( result.IsPartial );
        _deduplicatorMock.Verify(
            d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that the orchestrator returns an honestly-flagged partial result when the final
    /// result never arrives within the time budget.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenFinalNeverArrives_ShouldReturnHonestPartial( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        LookupSagaState partialSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialSaga );

        // Final never published - wait times out
        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( (string?)null );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.SagaId );
    }

    /// <summary>
    /// Verifies that the orchestrator returns the partial with rate limit info when the
    /// rate-limited sentinel is published while waiting for the final result.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenRateLimitSentinelDuringFinalWait_ShouldReturnPartialWithInfo( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        // First read: two providers pending, none rate-limited yet (so the wait proceeds);
        // second read (after sentinel): rate limit info recorded
        LookupSagaState pendingSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false), (SupportedProviders.Tidal, false)]
        );
        LookupSagaState rateLimitedSaga = pendingSaga with { RateLimitInfo = rateLimitInfo };

        _ = _sagaManagerMock
            .SetupSequence( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( pendingSaga )
            .ReturnsAsync( rateLimitedSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( LookupConstants.RateLimitedSentinel );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        Assert.AreEqual( SupportedProviders.AppleMusic, result.RateLimitedProviders[0].Provider );
    }

    /// <summary>
    /// Verifies that the orchestrator returns the partial immediately (without waiting)
    /// when every pending provider is rate-limited.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenAllPendingProvidersRateLimited_ShouldReturnPartialImmediately( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        LookupSagaState rateLimitedSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            rateLimitInfo: rateLimitInfo,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( rateLimitedSaga );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        _deduplicatorMock.Verify(
            d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that a second caller (already-in-flight branch) also waits for the final
    /// result when the published result is only partial.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithInFlightPartialResult_ShouldKeepWaitingForFinal( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationInFlight( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        LookupSagaState partialSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );
        LookupSagaState finalSaga = CreateSagaState(
            isPartial: false,
            partialResultUri: partialUri,
            finalResultUri: TestRecordUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, true)]
        );

        _ = _sagaManagerMock
            .SetupSequence( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialSaga )
            .ReturnsAsync( finalSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( TestRecordUri );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
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

    /// <summary>
    /// Verifies that resuming a saga that already stored a partial result resolves it
    /// directly without re-queueing the initial provider lookup.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenResumedSagaHasPartialResult_ShouldNotReenqueue( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        LookupSagaState resumedSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetOrCreateAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( resumedSaga );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( resumedSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( (string?)null );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        _queueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ) ),
            Times.Never
        );
        _deduplicatorMock.Verify(
            d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that resuming a saga with a stored result releases the just-acquired
    /// deduplication lock with the known result URI (instead of leaking it for its full
    /// TTL), so concurrently blocked waiters receive the stored result.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenResumedSagaHasStoredResult_ShouldReleaseLockWithKnownUri( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        LookupSagaState resumedSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetOrCreateAsync( It.IsAny<string>( ), It.IsAny<string>( ), It.IsAny<LookupRequestType>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( resumedSaga );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( resumedSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( (string?)null );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        _ = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - released with the known URI (not null: an empty publish would leave waiters stuck)
        _deduplicatorMock.Verify( d => d.ReleaseAsync( It.IsAny<string>( ), partialUri ), Times.Once );
        _deduplicatorMock.Verify( d => d.ReleaseAsync( It.IsAny<string>( ), null ), Times.Never );
    }

    /// <summary>
    /// Verifies that the already-in-flight branch skips the blind completion wait when the
    /// saga already stored a result and instead goes to the final-result wait, which honors
    /// the rate-limit escape hatch.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithInFlightSagaHavingStoredResult_ShouldSkipBlindWaitAndHonorEscapeHatch( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationInFlight( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        LookupSagaState rateLimitedSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            rateLimitInfo: rateLimitInfo,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( rateLimitedSaga );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - escape hatch returned the partial without ever blind-waiting
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        _deduplicatorMock.Verify(
            d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that the already-in-flight branch returns the saga's stored partial data
    /// (instead of a links-less result) when the rate-limited sentinel is received.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WithInFlightRateLimitSentinel_ShouldReturnStoredPartialData( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationInFlight( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        LookupSagaState rateLimitedSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            rateLimitInfo: rateLimitInfo,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        // First read (pre-wait check): no saga yet; second read (after sentinel): partial stored
        _ = _sagaManagerMock
            .SetupSequence( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( (LookupSagaState?)null )
            .ReturnsAsync( rateLimitedSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( LookupConstants.RateLimitedSentinel );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( partialUri ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - available partial links are returned alongside the rate-limit info
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        Assert.AreEqual( SupportedProviders.AppleMusic, result.RateLimitedProviders[0].Provider );
    }

    /// <summary>
    /// Verifies that expired rate-limit info does not trigger the escape hatch: the
    /// orchestrator keeps waiting and returns the final result when it arrives.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenAllPendingRateLimitsExpired_ShouldKeepWaitingForFinal( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        // Rate limit lapsed five minutes ago - waiting CAN help, escape hatch must not fire
        List<ProviderRateLimitInfo> expiredRateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( -5 ), "/v1/catalog" )
        ];

        LookupSagaState partialSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            rateLimitInfo: expiredRateLimitInfo,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );
        LookupSagaState finalSaga = CreateSagaState(
            isPartial: false,
            partialResultUri: partialUri,
            finalResultUri: TestRecordUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, true)]
        );

        _ = _sagaManagerMock
            .SetupSequence( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialSaga )
            .ReturnsAsync( finalSaga );

        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( TestRecordUri );

        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( expectedResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - the final result was waited for instead of returning a stale partial
        Assert.IsNotNull( result.Result );
        Assert.IsFalse( result.IsPartial );
        _deduplicatorMock.Verify(
            d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that expired rate-limit info is not reported to the caller when the wait
    /// budget runs out (no stale "retry after" messaging).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenRateLimitExpiredAndBudgetExhausted_ShouldNotReportExpiredRateLimits( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        List<ProviderRateLimitInfo> expiredRateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( -5 ), "/v1/catalog" )
        ];

        LookupSagaState partialSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            rateLimitInfo: expiredRateLimitInfo,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialSaga );

        // Final never published - wait times out
        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .ReturnsAsync( (string?)null );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNull( result.RateLimitedProviders );
    }

    /// <summary>
    /// Verifies that the creator-path timeout returns the final result when the saga has a
    /// FinalResultUri but no partial (e.g. ISRC/UPC direct lookups never write a partial).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenCreateWaitTimesOutWithFinalResult_ShouldReturnFinal( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        // No completion notification arrives within the budget
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( (string?)null );

        LookupSagaState finalSaga = CreateSagaState(
            isPartial: false,
            finalResultUri: TestRecordUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, true), (SupportedProviders.Tidal, true)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( finalSaga );

        MediaLinkResult finalResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( TestRecordUri ) )
            .ReturnsAsync( finalResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - the cached final result is returned, honestly flagged as complete
        Assert.IsNotNull( result.Result );
        Assert.IsFalse( result.IsPartial );
        Assert.IsNull( result.SagaId );
    }

    /// <summary>
    /// Verifies that the create path returns the saga's stored partial data (instead of a
    /// links-less result) when the rate-limited sentinel is received.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenRateLimitSentinelOnCreatePath_ShouldReturnStoredPartialData( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( LookupConstants.RateLimitedSentinel );

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        LookupSagaState rateLimitedSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            rateLimitInfo: rateLimitInfo,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );

        _ = _sagaManagerMock
            .Setup( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( rateLimitedSaga );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( partialUri ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - available partial links are returned alongside the rate-limit info
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        Assert.AreEqual( SupportedProviders.AppleMusic, result.RateLimitedProviders[0].Provider );
    }

    /// <summary>
    /// Verifies that a rate-limit condition arising in the subscribe gap (between reading
    /// saga state and the completion subscription becoming active) is detected by the
    /// missed-result check and resolved into a partial result via the sentinel.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task LookupByIsrcAsync_WhenRateLimitArisesInSubscribeGap_ShouldReturnPartialViaSentinel( ) {
        // Arrange
        SetupCacheMiss( );
        SetupDeduplicationAcquired( );
        SetupSagaCreation( );

        const string partialUri = "at://did:plc:test/com.bridgebeats.media.link/partial";

        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( partialUri );

        List<ProviderRateLimitInfo> rateLimitInfo = [
            new ProviderRateLimitInfo( SupportedProviders.AppleMusic, DateTimeOffset.UtcNow.AddMinutes( 5 ), "/v1/catalog" )
        ];

        LookupSagaState pendingSaga = CreateSagaState(
            isPartial: true,
            partialResultUri: partialUri,
            providers: [(SupportedProviders.Spotify, true), (SupportedProviders.AppleMusic, false)]
        );
        LookupSagaState rateLimitedSaga = pendingSaga with { RateLimitInfo = rateLimitInfo };

        // First read (wait loop): no rate limits yet; second read (gap check inside the
        // deduplicator): rate limit recorded; third read (sentinel handling): unchanged
        _ = _sagaManagerMock
            .SetupSequence( s => s.GetAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( pendingSaga )
            .ReturnsAsync( rateLimitedSaga )
            .ReturnsAsync( rateLimitedSaga );

        // Simulate the deduplicator running the missed-result check after subscribing
        _ = _deduplicatorMock
            .Setup( d => d.WaitForFinalCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ), It.IsAny<Func<Task<string?>>?>( ) ) )
            .Returns( ( string _, TimeSpan _, Func<Task<string?>>? missedResultCheck, CancellationToken _ ) => missedResultCheck!( ) );

        MediaLinkResult partialResult = CreateMediaLinkResult( );
        _ = _atProtoStorageMock
            .Setup( a => a.GetMediaLinkResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( partialResult );

        // Act
        LookupResult result = await _orchestrator.LookupByIsrcAsync( TestIsrc );

        // Assert - the gap check surfaced the sentinel and the partial was returned
        Assert.IsNotNull( result.Result );
        Assert.IsTrue( result.IsPartial );
        Assert.IsNotNull( result.RateLimitedProviders );
        Assert.AreEqual( SupportedProviders.AppleMusic, result.RateLimitedProviders[0].Provider );
    }

    /// <summary>
    /// Verifies that LookupByIsrcAsync releases the deduplication lock when an error occurs.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

    /// <summary>
    /// Creates a new <see cref="LookupOrchestrator"/> instance with the configured mocks.
    /// </summary>
    /// <returns>A new <see cref="LookupOrchestrator"/> instance.</returns>
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

    /// <summary>
    /// Creates a test <see cref="MediaLinkResult"/> with sample data.
    /// </summary>
    /// <returns>A new <see cref="MediaLinkResult"/> with Spotify test data.</returns>
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

    /// <summary>
    /// Creates a test <see cref="LookupSagaState"/> with the given provider completion states.
    /// </summary>
    private static LookupSagaState CreateSagaState(
        bool isPartial = false,
        string? partialResultUri = null,
        string? finalResultUri = null,
        List<ProviderRateLimitInfo>? rateLimitInfo = null,
        (SupportedProviders provider, bool isComplete)[]? providers = null
    ) {
        Dictionary<SupportedProviders, ProviderLookupState> states = [];
        foreach ((SupportedProviders provider, bool isComplete) in providers ?? []) {
            states[provider] = new ProviderLookupState(
                Provider: provider,
                IsComplete: isComplete,
                IsSuccess: isComplete,
                ResultJson: null,
                CompletedAt: isComplete ? DateTimeOffset.UtcNow : null,
                ErrorMessage: null
            );
        }

        return new LookupSagaState {
            SagaId = "test-saga",
            LookupKey = "test-key",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = TestIsrc,
            CreatedAt = DateTimeOffset.UtcNow,
            IsPartial = isPartial,
            PartialResultUri = partialResultUri,
            FinalResultUri = finalResultUri,
            ProviderStates = states,
            RateLimitInfo = rateLimitInfo
        };
    }

    /// <summary>
    /// Configures the cache mock to return null for ISRC lookups (cache miss).
    /// </summary>
    private void SetupCacheMiss( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByISRCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    /// <summary>
    /// Configures the cache mock to return null for UPC lookups (cache miss).
    /// </summary>
    private void SetupCacheMissForUpc( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByUPCAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    /// <summary>
    /// Configures the cache mock to return null for metadata lookups (cache miss).
    /// </summary>
    private void SetupCacheMissForMetadata( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByMetadataAsync( It.IsAny<string>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    /// <summary>
    /// Configures the cache mock to return null for provider ID lookups (cache miss).
    /// </summary>
    private void SetupCacheMissForProviderId( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultByProviderIdAsync( It.IsAny<string>( ), It.IsAny<SupportedProviders>( ), It.IsAny<bool>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    /// <summary>
    /// Configures the cache mock to return null for URL lookups (cache miss).
    /// </summary>
    private void SetupCacheMissForUrl( ) {
        _ = _cacheMock
            .Setup( c => c.TryGetCachedResultAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( ((MediaLinkResult result, string recordUri, bool isStale)?)null );
    }

    /// <summary>
    /// Configures the deduplicator mock to return a successful acquisition result.
    /// </summary>
    private void SetupDeduplicationAcquired( ) {
        _ = _deduplicatorMock
            .Setup( d => d.TryAcquireAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( new DeduplicationResult( Acquired: true, AlreadyInFlight: false, RequestKey: "test-key" ) );
    }

    /// <summary>
    /// Configures the deduplicator mock to indicate the request is already in-flight.
    /// </summary>
    private void SetupDeduplicationInFlight( ) {
        _ = _deduplicatorMock
            .Setup( d => d.TryAcquireAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( new DeduplicationResult( Acquired: false, AlreadyInFlight: true, RequestKey: "test-key" ) );
    }

    /// <summary>
    /// Configures the saga manager mock for saga creation and state management.
    /// </summary>
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

    /// <summary>
    /// Configures the deduplicator mock to return a result URI when waiting for completion.
    /// </summary>
    private void SetupDeduplicationWaitWithResult( ) {
        _ = _deduplicatorMock
            .Setup( d => d.WaitForCompletionAsync( It.IsAny<string>( ), It.IsAny<TimeSpan>( ) ) )
            .ReturnsAsync( TestRecordUri );
    }

    #endregion
}
