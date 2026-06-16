using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RedisGenreCache"/> (the <c>IGenreCacheService</c> implementation). Drive
/// the track-genre, artist-genre, track-artist-mapping, and artist-refresh-queue operations against a
/// mocked Redis database and verify: constructor null-guards; empty-id short-circuits that skip Redis;
/// hash-backed get/set keyed under <c>genre:</c> and <c>artist-genre:</c>; the sorted-set
/// artist-refresh queue (idempotent enqueue with <c>When.NotExists</c>, batch dequeue, length); and the
/// empty-input guards that avoid touching Redis.
/// </summary>
[TestClass]
public class RedisGenreCacheTests {
    /// <summary>Mocked Redis multiplexer returning <see cref="_databaseMock"/>.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mocked Redis database backing all cache operations.</summary>
    private Mock<IDatabase> _databaseMock = null!;
    /// <summary>Mocked logger for the cache under test.</summary>
    private Mock<ILogger<RedisGenreCache>> _loggerMock = null!;

    /// <summary>Representative track id used across tests.</summary>
    private const string TestTrackId = "track123";
    /// <summary>Representative artist id used across tests.</summary>
    private const string TestArtistId = "artist456";
    /// <summary>The provider these tests key cache entries under (<see cref="SupportedProviders.Spotify"/>).</summary>
    private const SupportedProviders TestProvider = SupportedProviders.Spotify;

    /// <summary>MSTest-injected context, used for per-test cancellation tokens.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Builds dependency mocks and wires the database before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _databaseMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<RedisGenreCache>>( );

        _ = _redisMock.Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) ).Returns( _databaseMock.Object );
    }

    #region Constructor Tests

    /// <summary>The constructor builds an instance when Redis and logger are supplied.</summary>
    [TestMethod]
    public void Constructor_WithValidDependencies_ShouldCreateInstance( ) {
        // Act
        RedisGenreCache cache = CreateCache( );

        // Assert
        Assert.IsNotNull( cache );
    }

    /// <summary>A null Redis multiplexer throws <see cref="ArgumentNullException"/> (param <c>redis</c>).</summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new RedisGenreCache( null!, _loggerMock.Object ) );
        Assert.AreEqual( "redis", ex.ParamName );
    }

    /// <summary>A null logger throws <see cref="ArgumentNullException"/> (param <c>logger</c>).</summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new RedisGenreCache( _redisMock.Object, null! ) );
        Assert.AreEqual( "logger", ex.ParamName );
    }

    #endregion

    #region GetGenresAsync Tests

    /// <summary>An empty track id short-circuits and returns <c>null</c> without querying Redis.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetGenresAsync_WithEmptyProviderId_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string>? result = await cache.GetGenresAsync( TestProvider, string.Empty, TestContext.CancellationToken );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>A cache miss (empty hash) returns <c>null</c>.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetGenresAsync_WithNoCacheEntry_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        _ = _databaseMock
            .Setup( d => d.HashGetAllAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        // Act
        IReadOnlyList<string>? result = await cache.GetGenresAsync( SupportedProviders.AppleMusic, TestTrackId, TestContext.CancellationToken );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// A cache hit deserializes the stored genres-JSON hash field and returns the genre list.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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
        IReadOnlyList<string>? result = await cache.GetGenresAsync( SupportedProviders.AppleMusic, TestTrackId, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( result );
        Assert.HasCount( 2, result );
        Assert.Contains( "Rock", result );
        Assert.Contains( "Alternative", result );
    }

    #endregion

    #region SetGenresAsync Tests

    /// <summary>An empty track id short-circuits and does not write to Redis.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SetGenresAsync_WithEmptyProviderId_ShouldNotCallRedis( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        await cache.SetGenresAsync( TestProvider, string.Empty, ["Rock"], TestContext.CancellationToken );

        // Assert
        _databaseMock.Verify(
            d => d.HashSetAsync( It.IsAny<RedisKey>( ), It.IsAny<HashEntry[]>( ), It.IsAny<CommandFlags>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Valid genres are written to a two-field hash under the <c>genre:{provider}:{trackId}</c> key.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SetGenresAsync_WithValidGenres_ShouldCacheGenres( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        List<string> genres = ["Rock", "Alternative", "Indie"];

        // Act
        await cache.SetGenresAsync( TestProvider, TestTrackId, genres, TestContext.CancellationToken );

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

    /// <summary>An empty artist id short-circuits and returns <c>null</c> without querying Redis.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetArtistGenresAsync_WithEmptyArtistId_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string>? result = await cache.GetArtistGenresAsync( TestProvider, string.Empty, TestContext.CancellationToken );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// A cache hit deserializes the stored artist-genres-JSON hash field and returns the genre list.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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
        IReadOnlyList<string>? result = await cache.GetArtistGenresAsync( TestProvider, TestArtistId, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( result );
        Assert.Contains( "Pop", result );
        Assert.Contains( "Electronic", result );
    }

    #endregion

    #region SetArtistGenresAsync Tests

    /// <summary>
    /// Valid artist genres are written to a two-field hash under the
    /// <c>artist-genre:{provider}:{artistId}</c> key.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SetArtistGenresAsync_WithValidGenres_ShouldCacheGenres( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        List<string> genres = ["Pop", "Dance"];

        // Act
        await cache.SetArtistGenresAsync( TestProvider, TestArtistId, genres, TestContext.CancellationToken );

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

    /// <summary>An empty track id short-circuits and returns <c>null</c> without querying Redis.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetTrackArtistMappingAsync_WithEmptyTrackId_ShouldReturnNull( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string>? result = await cache.GetTrackArtistMappingAsync( TestProvider, string.Empty, TestContext.CancellationToken );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// An empty artist-id list short-circuits and does not open a Redis transaction to write the
    /// track-artist mapping.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SetTrackArtistMappingAsync_WithEmptyArtistIds_ShouldNotCallRedis( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        Mock<ITransaction> transactionMock = new( );
        _ = _databaseMock.Setup( d => d.CreateTransaction( It.IsAny<object>( ) ) ).Returns( transactionMock.Object );

        // Act
        await cache.SetTrackArtistMappingAsync( TestProvider, TestTrackId, [], TestContext.CancellationToken );

        // Assert
        _databaseMock.Verify( d => d.CreateTransaction( It.IsAny<object>( ) ), Times.Never );
    }

    #endregion

    #region Artist Refresh Queue Tests

    /// <summary>
    /// An empty artist-id list short-circuits and does not add to the artist-refresh sorted set.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueArtistsForRefreshAsync_WithEmptyArtistIds_ShouldNotCallRedis( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        await cache.EnqueueArtistsForRefreshAsync( TestProvider, [], TestContext.CancellationToken );

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
    /// Each valid artist id is added once to the <c>artist-refresh-queue:</c> sorted set with
    /// <see cref="When.NotExists"/>, so re-enqueuing an in-flight artist is idempotent.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueArtistsForRefreshAsync_WithValidArtistIds_ShouldEnqueue( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        List<string> artistIds = ["artist1", "artist2"];

        // Act
        await cache.EnqueueArtistsForRefreshAsync( TestProvider, artistIds, TestContext.CancellationToken );

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

    /// <summary>A zero batch size returns an empty list without querying Redis.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DequeueArtistsForRefreshAsync_WithZeroBatchSize_ShouldReturnEmptyList( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );

        // Act
        IReadOnlyList<string> result = await cache.DequeueArtistsForRefreshAsync( TestProvider, 0, TestContext.CancellationToken );

        // Assert
        Assert.IsEmpty( result );
    }

    /// <summary>
    /// A batch dequeue pops sorted-set entries and returns their artist-id members.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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
        IReadOnlyList<string> result = await cache.DequeueArtistsForRefreshAsync( TestProvider, 50, TestContext.CancellationToken );

        // Assert
        Assert.HasCount( 2, result );
        Assert.Contains( "artist1", result );
        Assert.Contains( "artist2", result );
    }

    /// <summary>The queue-length query returns the artist-refresh sorted-set cardinality.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetArtistRefreshQueueLengthAsync_ShouldReturnQueueLength( ) {
        // Arrange
        RedisGenreCache cache = CreateCache( );
        _ = _databaseMock
            .Setup( d => d.SortedSetLengthAsync( It.IsAny<RedisKey>( ), It.IsAny<double>( ), It.IsAny<double>( ), It.IsAny<Exclude>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 42 );

        // Act
        long result = await cache.GetArtistRefreshQueueLengthAsync( TestProvider, TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( 42, result );
    }

    #endregion

    #region Helper Methods

    /// <summary>Builds a <see cref="RedisGenreCache"/> from the current Redis and logger mocks.</summary>
    private RedisGenreCache CreateCache( ) =>
        new( _redisMock.Object, _loggerMock.Object );

    #endregion
}
