using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
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

    [ClassInitialize]
    [Obsolete]
    public static async Task ClassInitialize( TestContext context ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

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
