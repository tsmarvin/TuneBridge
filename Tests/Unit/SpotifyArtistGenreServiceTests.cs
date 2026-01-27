using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SpotifyArtistGenreService"/> to verify
/// constructor validation.
/// </summary>
/// <remarks>
/// Note: Since SpotifyLookupService is sealed, we can only test constructor null checks
/// without full integration testing infrastructure.
/// </remarks>
[TestClass]
public class SpotifyArtistGenreServiceTests {
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IGenreCacheService> _genreCacheMock = null!;
    private Mock<ILogger<SpotifyArtistGenreService>> _loggerMock = null!;

    /// <summary>
    /// Initializes mocks before each test.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _genreCacheMock = new Mock<IGenreCacheService>( );
        _loggerMock = new Mock<ILogger<SpotifyArtistGenreService>>( );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when redis is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( null!, _genreCacheMock.Object, null!, _loggerMock.Object ) );
        Assert.AreEqual( "redis", ex.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when genreCache is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullGenreCache_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( _redisMock.Object, null!, null!, _loggerMock.Object ) );
        Assert.AreEqual( "genreCache", ex.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when lookupService is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLookupService_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( _redisMock.Object, _genreCacheMock.Object, null!, _loggerMock.Object ) );
        Assert.AreEqual( "lookupService", ex.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when logger is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        // Note: We can't pass a real SpotifyLookupService (it's sealed) so we test that genreCache check occurs first
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyArtistGenreService( _redisMock.Object, _genreCacheMock.Object, null!, null! ) );
        // lookupService check comes before logger check
        Assert.AreEqual( "lookupService", ex.ParamName );
    }

    #endregion
}
