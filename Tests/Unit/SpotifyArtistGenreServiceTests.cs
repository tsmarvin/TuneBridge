using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SpotifyArtistGenreService"/>, focused on constructor dependency
/// validation. Each test confirms that a null required dependency throws
/// <see cref="ArgumentNullException"/> with the expected parameter name, including the case where
/// the missing lookup service is reported before the missing logger.
/// </summary>
[TestClass]
public class SpotifyArtistGenreServiceTests {
    /// <summary>Mock Redis multiplexer passed as the <c>redis</c> dependency.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock genre cache passed as the <c>genreCache</c> dependency.</summary>
    private Mock<IGenreCacheService> _genreCacheMock = null!;
    /// <summary>Mock logger passed as the <c>logger</c> dependency.</summary>
    private Mock<ILogger<SpotifyArtistGenreService>> _loggerMock = null!;

    /// <summary>
    /// Creates fresh mocks for the Redis multiplexer, genre cache, and logger before each test.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _genreCacheMock = new Mock<IGenreCacheService>( );
        _loggerMock = new Mock<ILogger<SpotifyArtistGenreService>>( );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that a null Redis multiplexer throws <see cref="ArgumentNullException"/> with
    /// parameter name <c>redis</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( null!, _genreCacheMock.Object, null!, _loggerMock.Object ) );
        Assert.AreEqual( "redis", ex.ParamName );
    }

    /// <summary>
    /// Verifies that a null genre cache throws <see cref="ArgumentNullException"/> with parameter
    /// name <c>genreCache</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullGenreCache_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( _redisMock.Object, null!, null!, _loggerMock.Object ) );
        Assert.AreEqual( "genreCache", ex.ParamName );
    }

    /// <summary>
    /// Verifies that a null lookup service throws <see cref="ArgumentNullException"/> with parameter
    /// name <c>lookupService</c>.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLookupService_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( _redisMock.Object, _genreCacheMock.Object, null!, _loggerMock.Object ) );
        Assert.AreEqual( "lookupService", ex.ParamName );
    }

    /// <summary>
    /// Verifies that when both the lookup service and the logger are null, the constructor reports
    /// the lookup service first: it throws <see cref="ArgumentNullException"/> with parameter name
    /// <c>lookupService</c>, confirming argument validation order places that guard ahead of the
    /// logger guard.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( _redisMock.Object, _genreCacheMock.Object, null!, null! ) );
        // lookupService check comes before logger check
        Assert.AreEqual( "lookupService", ex.ParamName );
    }

    #endregion
}
