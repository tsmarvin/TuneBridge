using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Services.LinkResolver;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="CachingMediaLinkService"/> to verify proper delegation
/// to the orchestrator and partial result message handling.
/// </summary>
[TestClass]
public class CachingMediaLinkServiceTests {
    private Mock<ILookupOrchestrator> _orchestratorMock = null!;
    private Mock<ILogger<CachingMediaLinkService>> _loggerMock = null!;
    private CachingMediaLinkService _service = null!;

    private const string TestIsrc = "USRC17607839";
    private const string TestUpc = "012345678901";
    private const string TestTitle = "Test Song";
    private const string TestArtist = "Test Artist";
    private const string TestProviderId = "spotify-123";
    private const string TestContent = "https://open.spotify.com/track/abc123";

    /// <summary>
    /// Initializes test dependencies before each test method.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _orchestratorMock = new Mock<ILookupOrchestrator>( );
        _loggerMock = new Mock<ILogger<CachingMediaLinkService>>( );
        _service = new CachingMediaLinkService( _orchestratorMock.Object, _loggerMock.Object );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor creates a valid instance when provided with valid dependencies.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        CachingMediaLinkService service = new( _orchestratorMock.Object, _loggerMock.Object );

        // Assert
        Assert.IsNotNull( service );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the orchestrator is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullOrchestrator_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new CachingMediaLinkService( null!, _loggerMock.Object )
        );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when the logger is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            new CachingMediaLinkService( _orchestratorMock.Object, null! )
        );
    }

    #endregion

    #region GetInfoAsync (Metadata) Tests

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoAsync(string, string)"/> properly
    /// delegates metadata-based lookups to the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_WithMetadata_ShouldDelegateToOrchestrator( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _orchestratorMock
            .Setup( o => o.LookupByMetadataAsync( TestTitle, TestArtist ) )
            .ReturnsAsync( new LookupResult { Result = expectedResult, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoAsync( TestTitle, TestArtist );

        // Assert
        Assert.IsNotNull( result );
        _orchestratorMock.Verify( o => o.LookupByMetadataAsync( TestTitle, TestArtist ), Times.Once );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoAsync(string, string)"/> returns null
    /// when the orchestrator returns a null result.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_WhenOrchestratorReturnsNull_ShouldReturnNull( ) {
        // Arrange
        _ = _orchestratorMock
            .Setup( o => o.LookupByMetadataAsync( It.IsAny<string>( ), It.IsAny<string>( ) ) )
            .ReturnsAsync( new LookupResult { Result = null, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoAsync( TestTitle, TestArtist );

        // Assert
        Assert.IsNull( result );
    }

    #endregion

    #region GetInfoByISRCAsync Tests

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByISRCAsync"/> properly
    /// delegates ISRC-based lookups to the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByISRCAsync_ShouldDelegateToOrchestrator( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _orchestratorMock
            .Setup( o => o.LookupByIsrcAsync( TestIsrc ) )
            .ReturnsAsync( new LookupResult { Result = expectedResult, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByISRCAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result );
        _orchestratorMock.Verify( o => o.LookupByIsrcAsync( TestIsrc ), Times.Once );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByISRCAsync"/> returns null
    /// when the orchestrator returns a null result.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByISRCAsync_WhenOrchestratorReturnsNull_ShouldReturnNull( ) {
        // Arrange
        _ = _orchestratorMock
            .Setup( o => o.LookupByIsrcAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( new LookupResult { Result = null, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByISRCAsync( TestIsrc );

        // Assert
        Assert.IsNull( result );
    }

    #endregion

    #region GetInfoByUPCAsync Tests

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByUPCAsync"/> properly
    /// delegates UPC-based lookups to the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByUPCAsync_ShouldDelegateToOrchestrator( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _orchestratorMock
            .Setup( o => o.LookupByUpcAsync( TestUpc ) )
            .ReturnsAsync( new LookupResult { Result = expectedResult, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByUPCAsync( TestUpc );

        // Assert
        Assert.IsNotNull( result );
        _orchestratorMock.Verify( o => o.LookupByUpcAsync( TestUpc ), Times.Once );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByUPCAsync"/> returns null
    /// when the orchestrator returns a null result.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByUPCAsync_WhenOrchestratorReturnsNull_ShouldReturnNull( ) {
        // Arrange
        _ = _orchestratorMock
            .Setup( o => o.LookupByUpcAsync( It.IsAny<string>( ) ) )
            .ReturnsAsync( new LookupResult { Result = null, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByUPCAsync( TestUpc );

        // Assert
        Assert.IsNull( result );
    }

    #endregion

    #region GetInfoByProviderIdAsync Tests

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByProviderIdAsync"/> properly
    /// delegates track lookups by provider ID to the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByProviderIdAsync_ForTrack_ShouldDelegateToOrchestrator( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _orchestratorMock
            .Setup( o => o.LookupByProviderIdAsync( TestProviderId, SupportedProviders.Spotify, false ) )
            .ReturnsAsync( new LookupResult { Result = expectedResult, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByProviderIdAsync( TestProviderId, SupportedProviders.Spotify, false );

        // Assert
        Assert.IsNotNull( result );
        _orchestratorMock.Verify(
            o => o.LookupByProviderIdAsync( TestProviderId, SupportedProviders.Spotify, false ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByProviderIdAsync"/> properly
    /// delegates album lookups by provider ID to the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByProviderIdAsync_ForAlbum_ShouldDelegateToOrchestrator( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _orchestratorMock
            .Setup( o => o.LookupByProviderIdAsync( TestProviderId, SupportedProviders.AppleMusic, true ) )
            .ReturnsAsync( new LookupResult { Result = expectedResult, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByProviderIdAsync( TestProviderId, SupportedProviders.AppleMusic, true );

        // Assert
        Assert.IsNotNull( result );
        _orchestratorMock.Verify(
            o => o.LookupByProviderIdAsync( TestProviderId, SupportedProviders.AppleMusic, true ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoByProviderIdAsync"/> returns null
    /// when the orchestrator returns a null result.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByProviderIdAsync_WhenOrchestratorReturnsNull_ShouldReturnNull( ) {
        // Arrange
        _ = _orchestratorMock
            .Setup( o => o.LookupByProviderIdAsync( It.IsAny<string>( ), It.IsAny<SupportedProviders>( ), It.IsAny<bool>( ) ) )
            .ReturnsAsync( new LookupResult { Result = null, IsPartial = false } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByProviderIdAsync( TestProviderId, SupportedProviders.Spotify, false );

        // Assert
        Assert.IsNull( result );
    }

    #endregion

    #region GetInfoAsync (Content) Tests

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoAsync(string)"/> properly
    /// delegates content-based lookups to the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_WithContent_ShouldDelegateToOrchestrator( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        _ = _orchestratorMock
            .Setup( o => o.LookupByContentAsync( TestContent ) )
            .Returns( CreateAsyncEnumerable( new LookupResult { Result = expectedResult, IsPartial = false } ) );

        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _service.GetInfoAsync( TestContent )) {
            results.Add( result );
        }

        // Assert
        Assert.HasCount( 1, results );
        Assert.IsNotNull( results[0] );
        _orchestratorMock.Verify( o => o.LookupByContentAsync( TestContent ), Times.Once );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoAsync(string)"/> filters out
    /// null results returned by the orchestrator.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_WithContentReturningNull_ShouldSkipNullResults( ) {
        // Arrange
        _ = _orchestratorMock
            .Setup( o => o.LookupByContentAsync( TestContent ) )
            .Returns( CreateAsyncEnumerable(
                new LookupResult { Result = null, IsPartial = false },
                new LookupResult { Result = CreateMediaLinkResult( ), IsPartial = false }
            ) );

        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _service.GetInfoAsync( TestContent )) {
            results.Add( result );
        }

        // Assert - should only have the non-null result
        Assert.HasCount( 1, results );
    }

    /// <summary>
    /// Verifies that <see cref="CachingMediaLinkService.GetInfoAsync(string)"/> returns
    /// an empty sequence when no results are found.
    /// </summary>
    [TestMethod]
    public async Task GetInfoAsync_WithNoResults_ShouldReturnEmpty( ) {
        // Arrange
        _ = _orchestratorMock
            .Setup( o => o.LookupByContentAsync( It.IsAny<string>( ) ) )
            .Returns( CreateAsyncEnumerable<LookupResult>( ) );

        // Act
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _service.GetInfoAsync( TestContent )) {
            results.Add( result );
        }

        // Assert
        Assert.IsEmpty( results );
    }

    #endregion

    #region Partial Result Message Tests

    /// <summary>
    /// Verifies that when a partial result is returned due to rate limiting,
    /// the appropriate rate limit message is added to the result.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByISRCAsync_WhenPartial_ShouldAddRateLimitMessage( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );

        _ = _orchestratorMock
            .Setup( o => o.LookupByIsrcAsync( TestIsrc ) )
            .ReturnsAsync( new LookupResult {
                Result = expectedResult,
                IsPartial = true,
                SagaId = "test-saga",
                RateLimitedProviders = [
                    new ProviderRateLimitInfo( SupportedProviders.AppleMusic, retryAfter, "/v1/catalog" )
                ]
            } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByISRCAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNotNull( result.Messages );
        Assert.HasCount( 1, result.Messages );
        Assert.Contains( "AppleMusic", result.Messages[0] );
        Assert.Contains( "temporarily unavailable", result.Messages[0] );
    }

    /// <summary>
    /// Verifies that when multiple providers are rate limited, a message is added
    /// for each rate-limited provider.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByISRCAsync_WhenPartialWithMultipleRateLimited_ShouldAddAllMessages( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );

        _ = _orchestratorMock
            .Setup( o => o.LookupByIsrcAsync( TestIsrc ) )
            .ReturnsAsync( new LookupResult {
                Result = expectedResult,
                IsPartial = true,
                SagaId = "test-saga",
                RateLimitedProviders = [
                    new ProviderRateLimitInfo( SupportedProviders.AppleMusic, retryAfter, "/v1/catalog" ),
                    new ProviderRateLimitInfo( SupportedProviders.Tidal, retryAfter.AddMinutes( 2 ), "/v1/tracks" )
                ]
            } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByISRCAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNotNull( result.Messages );
        Assert.HasCount( 2, result.Messages );
        Assert.Contains( "AppleMusic", result.Messages[0] );
        Assert.Contains( "Tidal", result.Messages[1] );
    }

    /// <summary>
    /// Verifies that no messages are added when the result is complete (not partial).
    /// </summary>
    [TestMethod]
    public async Task GetInfoByISRCAsync_WhenNotPartial_ShouldNotAddMessages( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );

        _ = _orchestratorMock
            .Setup( o => o.LookupByIsrcAsync( TestIsrc ) )
            .ReturnsAsync( new LookupResult {
                Result = expectedResult,
                IsPartial = false
            } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByISRCAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNull( result.Messages );
    }

    /// <summary>
    /// Verifies that no messages are added when the result is partial but there
    /// are no rate-limited providers in the response.
    /// </summary>
    [TestMethod]
    public async Task GetInfoByISRCAsync_WhenPartialButNoRateLimitedProviders_ShouldNotAddMessages( ) {
        // Arrange
        MediaLinkResult expectedResult = CreateMediaLinkResult( );

        _ = _orchestratorMock
            .Setup( o => o.LookupByIsrcAsync( TestIsrc ) )
            .ReturnsAsync( new LookupResult {
                Result = expectedResult,
                IsPartial = true,
                RateLimitedProviders = []
            } );

        // Act
        MediaLinkResult? result = await _service.GetInfoByISRCAsync( TestIsrc );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsNull( result.Messages );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test <see cref="MediaLinkResult"/> with Spotify provider data for use in tests.
    /// </summary>
    /// <returns>A populated <see cref="MediaLinkResult"/> instance.</returns>
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
    /// Creates an <see cref="IAsyncEnumerable{T}"/> from the provided items for use in async enumeration tests.
    /// </summary>
    /// <typeparam name="T">The type of items in the enumerable.</typeparam>
    /// <param name="items">The items to include in the async enumerable.</param>
    /// <returns>An async enumerable yielding the provided items.</returns>
    private static async IAsyncEnumerable<T> CreateAsyncEnumerable<T>( params T[] items ) {
        foreach (T item in items) {
            await Task.Yield( );
            yield return item;
        }
    }

    #endregion
}
