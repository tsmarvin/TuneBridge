using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisRateLimitTracker"/> using the shared Redis container.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class RedisRateLimitTrackerTests {

    private static IConnectionMultiplexer? s_redis;

    private Mock<ILogger<RedisRateLimitTracker>> _mockLogger = null!;
    private RedisRateLimitTracker _tracker = null!;

    /// <summary>
    /// Initializes the shared Redis connection for all tests in this class.
    /// </summary>
    /// <param name="_">The test context provided by MSTest (unused).</param>
    [ClassInitialize]
    public static async Task ClassInitialize( TestContext _ ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Cleans up the Redis connection after all tests in this class have completed.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears rate limit keys and creates a fresh tracker before each test.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        // Clear only rate limit-related keys before each test
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "ratelimit:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _mockLogger = new Mock<ILogger<RedisRateLimitTracker>>( );

        _tracker = new RedisRateLimitTracker(
            s_redis,
            _mockLogger.Object
        );
    }

    /// <summary>
    /// Verifies that GetStateAsync returns not rate limited when no entry exists.
    /// </summary>
    [TestMethod]
    public async Task GetStateAsync_ReturnsNotRateLimited_WhenNoEntry( ) {
        // Act
        RateLimitState state = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );

        // Assert
        Assert.IsFalse( state.IsRateLimited );
        Assert.IsNull( state.RetryAfter );
        Assert.IsNull( state.TimeRemaining );
    }

    /// <summary>
    /// Verifies that SetRateLimitedAsync stores rate limit with correct TTL.
    /// </summary>
    [TestMethod]
    public async Task SetRateLimitedAsync_StoresRateLimitWithTtl( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddSeconds( 30 );

        // Act
        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter
        );

        // Assert
        RateLimitState state = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );

        Assert.IsTrue( state.IsRateLimited );
        Assert.IsNotNull( state.RetryAfter );
        Assert.IsNotNull( state.TimeRemaining );

        // RetryAfter should be close to what we set (within a second due to timing)
        TimeSpan diff = (retryAfter - state.RetryAfter.Value).Duration( );
        Assert.IsTrue( diff < TimeSpan.FromSeconds( 1 ), $"RetryAfter diff was {diff}" );
    }

    /// <summary>
    /// Verifies that SetRateLimitedAsync does not store already-expired rate limits.
    /// </summary>
    [TestMethod]
    public async Task SetRateLimitedAsync_DoesNotStore_WhenAlreadyExpired( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddSeconds( -10 );

        // Act
        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter
        );

        // Assert - should not be stored
        RateLimitState state = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );

        Assert.IsFalse( state.IsRateLimited );
    }

    /// <summary>
    /// Verifies that ClearAsync removes an existing rate limit.
    /// </summary>
    [TestMethod]
    public async Task ClearAsync_RemovesRateLimit( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );
        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter
        );

        // Verify it's set
        RateLimitState stateBefore = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );
        Assert.IsTrue( stateBefore.IsRateLimited );

        // Act
        await _tracker.ClearAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );

        // Assert
        RateLimitState stateAfter = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );
        Assert.IsFalse( stateAfter.IsRateLimited );
    }

    /// <summary>
    /// Verifies that GetAllRateLimitedAsync returns all rate-limited endpoints for a provider.
    /// </summary>
    [TestMethod]
    public async Task GetAllRateLimitedAsync_ReturnsAllEndpointsForProvider( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );

        await _tracker.SetRateLimitedAsync( SupportedProviders.Spotify, "/v1/search", retryAfter );
        await _tracker.SetRateLimitedAsync( SupportedProviders.Spotify, "/v1/tracks", retryAfter.AddMinutes( 1 ) );
        await _tracker.SetRateLimitedAsync( SupportedProviders.AppleMusic, "/v1/catalog", retryAfter );

        // Act
        IReadOnlyList<RateLimitedEndpoint> spotifyEndpoints =
            await _tracker.GetAllRateLimitedAsync( SupportedProviders.Spotify );

        IReadOnlyList<RateLimitedEndpoint> appleEndpoints =
            await _tracker.GetAllRateLimitedAsync( SupportedProviders.AppleMusic );

        // Assert
        Assert.HasCount( 2, spotifyEndpoints );
        Assert.HasCount( 1, appleEndpoints );

        Assert.IsTrue( spotifyEndpoints.Any( e => e.Endpoint == "/v1/search" ) );
        Assert.IsTrue( spotifyEndpoints.Any( e => e.Endpoint == "/v1/tracks" ) );
        Assert.IsTrue( appleEndpoints.Any( e => e.Endpoint == "/v1/catalog" ) );
    }

    /// <summary>
    /// Verifies that different providers have isolated rate limits.
    /// </summary>
    [TestMethod]
    public async Task DifferentProviders_HaveIsolatedRateLimits( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );

        await _tracker.SetRateLimitedAsync( SupportedProviders.Spotify, "/search", retryAfter );

        // Act
        RateLimitState spotifyState = await _tracker.GetStateAsync( SupportedProviders.Spotify, "/search" );
        RateLimitState appleState = await _tracker.GetStateAsync( SupportedProviders.AppleMusic, "/search" );
        RateLimitState tidalState = await _tracker.GetStateAsync( SupportedProviders.Tidal, "/search" );

        // Assert - only Spotify should be rate limited
        Assert.IsTrue( spotifyState.IsRateLimited );
        Assert.IsFalse( appleState.IsRateLimited );
        Assert.IsFalse( tidalState.IsRateLimited );
    }

    /// <summary>
    /// Verifies that rate limits expire automatically based on TTL.
    /// </summary>
    [TestMethod]
    public async Task RateLimit_ExpiresAutomatically( ) {
        // Arrange - set a very short TTL
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMilliseconds( 500 );

        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter
        );

        // Verify it's set
        RateLimitState stateBefore = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );
        Assert.IsTrue( stateBefore.IsRateLimited );

        // Wait for expiration
        await Task.Delay( 600 );

        // Act
        RateLimitState stateAfter = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search"
        );

        // Assert - should be expired
        Assert.IsFalse( stateAfter.IsRateLimited );
    }
}
