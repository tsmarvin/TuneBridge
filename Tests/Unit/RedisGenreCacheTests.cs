using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RedisGenreCache"/> to verify
/// genre caching, artist genre caching, track-artist mapping, and queue operations.
/// </summary>
[TestClass]
public class RedisGenreCacheTests {
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _databaseMock = null!;
    private Mock<ILogger<RedisGenreCache>> _loggerMock = null!;

    private const string TestTrackId = "track123";
    private const string TestArtistId = "artist456";
    private const SupportedProviders TestProvider = SupportedProviders.Spotify;

    /// <summary>
    /// Initializes mocks before each test.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _databaseMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<RedisGenreCache>>( );

        _ = _redisMock.Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) ).Returns( _databaseMock.Object );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor creates a valid instance with valid dependencies.
    /// </summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        RedisGenreCache cache = CreateCache( );

        // Assert
        Assert.IsNotNull( cache );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when Redis is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new RedisGenreCache( null!, _loggerMock.Object ) );
        Assert.AreEqual( "redis", ex.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws <see cref="ArgumentNullException"/> when logger is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new RedisGenreCache( _redisMock.Object, null! ) );
        Assert.AreEqual( "logger", ex.ParamName );
    }

    #endregion

    #region GetGenresAsync Tests

    /// <summary>
    /// Verifies that GetGenresAsync returns null when the providerId is empty.
    /// </summary>
    [TestMethod]
    public async Task GetGenresAsync_WithEmptyProviderId_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string>? result = await cache.GetGenresAsync( TestProvider, string.Empty );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that GetGenresAsync returns null when no cache entry exists.
    /// </summary>
    [TestMethod]
    public async Task GetGenresAsync_WithNoCacheEntry_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        _ = _databaseMock
            .Setup( d => d.HashGetAllAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        // Act
        IReadOnlyList<string>? result = await cache.GetGenresAsync( SupportedProviders.AppleMusic, TestTrackId );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that GetGenresAsync returns cached genres when entry exists.
    /// </summary>
    [TestMethod]
    public async Task GetGenresAsync_WithCachedEntry_ShouldReturnGenres( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        string genresJson = "[\"Rock\",\"Alternative\"]";
        HashEntry[] hashEntries = [
            new HashEntry( "genres", genresJson ),
            new HashEntry( "cachedAt", "2024-01-01T00:00:00Z" )
        ];
        _ = _databaseMock
            .Setup( d => d.HashGetAllAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( hashEntries );

        // Act
        IReadOnlyList<string>? result = await cache.GetGenresAsync( SupportedProviders.AppleMusic, TestTrackId );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 2, result );
        Assert.IsTrue( result.Contains( "Rock" ) );
        Assert.IsTrue( result.Contains( "Alternative" ) );
    }

    #endregion

    #region SetGenresAsync Tests

    /// <summary>
    /// Verifies that SetGenresAsync does nothing when providerId is empty.
    /// </summary>
    [TestMethod]
    public async Task SetGenresAsync_WithEmptyProviderId_ShouldNotCallRedis( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        await cache.SetGenresAsync( TestProvider, string.Empty, ["Rock"] );

        // Assert
        _databaseMock.Verify(
            d => d.HashSetAsync( It.IsAny<RedisKey>( ), It.IsAny<HashEntry[]>( ), It.IsAny<CommandFlags>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that SetGenresAsync caches genres correctly.
    /// </summary>
    [TestMethod]
    public async Task SetGenresAsync_WithValidGenres_ShouldCacheGenres( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        List<string> genres = ["Rock", "Alternative", "Indie"];

        // Act
        await cache.SetGenresAsync( TestProvider, TestTrackId, genres );

        // Assert
        _databaseMock.Verify(
            d => d.HashSetAsync(
                It.Is<RedisKey>( k => k.ToString( ).Contains( $"genre:{TestProvider}:{TestTrackId}" ) ),
                It.Is<HashEntry[]>( h => h.Length == 2 ),
                It.IsAny<CommandFlags>( )
            ),
            Times.Once
        );
    }

    #endregion

    #region GetArtistGenresAsync Tests

    /// <summary>
    /// Verifies that GetArtistGenresAsync returns null when artistId is empty.
    /// </summary>
    [TestMethod]
    public async Task GetArtistGenresAsync_WithEmptyArtistId_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string>? result = await cache.GetArtistGenresAsync( TestProvider, string.Empty );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that GetArtistGenresAsync returns cached genres when entry exists.
    /// </summary>
    [TestMethod]
    public async Task GetArtistGenresAsync_WithCachedEntry_ShouldReturnGenres( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        string genresJson = "[\"Pop\",\"Electronic\"]";
        HashEntry[] hashEntries = [
            new HashEntry( "genres", genresJson ),
            new HashEntry( "cachedAt", "2024-01-01T00:00:00Z" )
        ];
        _ = _databaseMock
            .Setup( d => d.HashGetAllAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( hashEntries );

        // Act
        IReadOnlyList<string>? result = await cache.GetArtistGenresAsync( TestProvider, TestArtistId );

        // Assert
        Assert.IsNotNull( result );
        Assert.IsTrue( result.Contains( "Pop" ) );
        Assert.IsTrue( result.Contains( "Electronic" ) );
    }

    #endregion

    #region SetArtistGenresAsync Tests

    /// <summary>
    /// Verifies that SetArtistGenresAsync caches artist genres correctly.
    /// </summary>
    [TestMethod]
    public async Task SetArtistGenresAsync_WithValidGenres_ShouldCacheGenres( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        List<string> genres = ["Pop", "Dance"];

        // Act
        await cache.SetArtistGenresAsync( TestProvider, TestArtistId, genres );

        // Assert
        _databaseMock.Verify(
            d => d.HashSetAsync(
                It.Is<RedisKey>( k => k.ToString( ).Contains( $"artist-genre:{TestProvider}:{TestArtistId}" ) ),
                It.Is<HashEntry[]>( h => h.Length == 2 ),
                It.IsAny<CommandFlags>( )
            ),
            Times.Once
        );
    }

    #endregion

    #region Track Artist Mapping Tests

    /// <summary>
    /// Verifies that GetTrackArtistMappingAsync returns null when trackId is empty.
    /// </summary>
    [TestMethod]
    public async Task GetTrackArtistMappingAsync_WithEmptyTrackId_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string>? result = await cache.GetTrackArtistMappingAsync( TestProvider, string.Empty );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that SetTrackArtistMappingAsync does nothing when artistIds is empty.
    /// </summary>
    [TestMethod]
    public async Task SetTrackArtistMappingAsync_WithEmptyArtistIds_ShouldNotCallRedis( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        Mock<ITransaction> transactionMock = new( );
        _ = _databaseMock.Setup( d => d.CreateTransaction( It.IsAny<object>( ) ) ).Returns( transactionMock.Object );

        // Act
        await cache.SetTrackArtistMappingAsync( TestProvider, TestTrackId, [] );

        // Assert
        _databaseMock.Verify( d => d.CreateTransaction( It.IsAny<object>( ) ), Times.Never );
    }

    #endregion

    #region Artist Refresh Queue Tests

    /// <summary>
    /// Verifies that EnqueueArtistsForRefreshAsync filters empty artist IDs.
    /// </summary>
    [TestMethod]
    public async Task EnqueueArtistsForRefreshAsync_WithEmptyArtistIds_ShouldNotCallRedis( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        await cache.EnqueueArtistsForRefreshAsync( TestProvider, [] );

        // Assert
        _databaseMock.Verify(
            d => d.SortedSetAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<double>( ),
                It.IsAny<When>( ),
                It.IsAny<CommandFlags>( )
            ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that EnqueueArtistsForRefreshAsync enqueues artists correctly.
    /// </summary>
    [TestMethod]
    public async Task EnqueueArtistsForRefreshAsync_WithValidArtistIds_ShouldEnqueue( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        List<string> artistIds = ["artist1", "artist2"];

        // Act
        await cache.EnqueueArtistsForRefreshAsync( TestProvider, artistIds );

        // Assert
        _databaseMock.Verify(
            d => d.SortedSetAddAsync(
                It.Is<RedisKey>( k => k.ToString( ).Contains( "artist-refresh-queue:" ) ),
                It.IsAny<RedisValue>( ),
                It.IsAny<double>( ),
                When.NotExists,
                It.IsAny<CommandFlags>( )
            ),
            Times.Exactly( 2 )
        );
    }

    /// <summary>
    /// Verifies that DequeueArtistsForRefreshAsync returns empty list when batchSize is zero.
    /// </summary>
    [TestMethod]
    public async Task DequeueArtistsForRefreshAsync_WithZeroBatchSize_ShouldReturnEmptyList( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string> result = await cache.DequeueArtistsForRefreshAsync( TestProvider, 0 );

        // Assert
        Assert.IsEmpty( result );
    }

    /// <summary>
    /// Verifies that DequeueArtistsForRefreshAsync returns artists from queue.
    /// </summary>
    [TestMethod]
    public async Task DequeueArtistsForRefreshAsync_WithQueuedArtists_ShouldReturnArtists( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        SortedSetEntry[] entries = [
            new SortedSetEntry( "artist1", 1000 ),
            new SortedSetEntry( "artist2", 1001 )
        ];
        _ = _databaseMock
            .Setup( d => d.SortedSetPopAsync( It.IsAny<RedisKey>( ), It.IsAny<long>( ), It.IsAny<Order>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( entries );

        // Act
        IReadOnlyList<string> result = await cache.DequeueArtistsForRefreshAsync( TestProvider, 50 );

        // Assert
        Assert.HasCount( 2, result );
        Assert.IsTrue( result.Contains( "artist1" ) );
        Assert.IsTrue( result.Contains( "artist2" ) );
    }

    /// <summary>
    /// Verifies that GetArtistRefreshQueueLengthAsync returns the queue length.
    /// </summary>
    [TestMethod]
    public async Task GetArtistRefreshQueueLengthAsync_ShouldReturnQueueLength( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        _ = _databaseMock
            .Setup( d => d.SortedSetLengthAsync( It.IsAny<RedisKey>( ), It.IsAny<double>( ), It.IsAny<double>( ), It.IsAny<Exclude>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 42 );

        // Act
        long result = await cache.GetArtistRefreshQueueLengthAsync( TestProvider );

        // Assert
        Assert.AreEqual( 42, result );
    }

    #endregion

    #region Helper Methods

    private RedisGenreCache CreateCache( ) =>
        new( _redisMock.Object, _loggerMock.Object );

    #endregion
}
