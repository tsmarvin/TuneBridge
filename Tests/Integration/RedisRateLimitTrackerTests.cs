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
    /// Gets or sets the test context which provides information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

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
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetStateAsync_ReturnsNotRateLimited_WhenNoEntry( ) {
        // Act
        RateLimitState state = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
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
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SetRateLimitedAsync_StoresRateLimitWithTtl( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddSeconds( 30 );

        // Act
        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter,
            TestContext.CancellationToken
        );

        // Assert
        RateLimitState state = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );

        Assert.IsTrue( state.IsRateLimited );
        Assert.IsNotNull( state.RetryAfter );
        Assert.IsNotNull( state.TimeRemaining );

        // RetryAfter should be close to what we set (within a second due to timing)
        TimeSpan diff = (retryAfter - state.RetryAfter.Value).Duration( );
        Assert.IsLessThan( diff, TimeSpan.FromSeconds( 1 ), $"RetryAfter diff was {diff}" );
    }

    /// <summary>
    /// Verifies that SetRateLimitedAsync does not store already-expired rate limits.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task SetRateLimitedAsync_DoesNotStore_WhenAlreadyExpired( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddSeconds( -10 );

        // Act
        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter,
            TestContext.CancellationToken
        );

        // Assert - should not be stored
        RateLimitState state = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );

        Assert.IsFalse( state.IsRateLimited );
    }

    /// <summary>
    /// Verifies that ClearAsync removes an existing rate limit.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ClearAsync_RemovesRateLimit( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );
        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter,
            TestContext.CancellationToken
        );

        // Verify it's set
        RateLimitState stateBefore = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );
        Assert.IsTrue( stateBefore.IsRateLimited );

        // Act
        await _tracker.ClearAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );

        // Assert
        RateLimitState stateAfter = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );
        Assert.IsFalse( stateAfter.IsRateLimited );
    }

    /// <summary>
    /// Verifies that GetAllRateLimitedAsync returns all rate-limited endpoints for a provider.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetAllRateLimitedAsync_ReturnsAllEndpointsForProvider( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );

        await _tracker.SetRateLimitedAsync( SupportedProviders.Spotify, "/v1/search", retryAfter, TestContext.CancellationToken );
        await _tracker.SetRateLimitedAsync( SupportedProviders.Spotify, "/v1/tracks", retryAfter.AddMinutes( 1 ), TestContext.CancellationToken );
        await _tracker.SetRateLimitedAsync( SupportedProviders.AppleMusic, "/v1/catalog", retryAfter, TestContext.CancellationToken );

        // Act
        IReadOnlyList<RateLimitedEndpoint> spotifyEndpoints =
            await _tracker.GetAllRateLimitedAsync( SupportedProviders.Spotify, TestContext.CancellationToken );

        IReadOnlyList<RateLimitedEndpoint> appleEndpoints =
            await _tracker.GetAllRateLimitedAsync( SupportedProviders.AppleMusic, TestContext.CancellationToken );

        // Assert
        Assert.HasCount( 2, spotifyEndpoints );
        Assert.HasCount( 1, appleEndpoints );

        Assert.Contains<RateLimitedEndpoint>( e => e.Endpoint == "/v1/search", spotifyEndpoints );
        Assert.Contains<RateLimitedEndpoint>( e => e.Endpoint == "/v1/tracks", spotifyEndpoints );
        Assert.Contains<RateLimitedEndpoint>( e => e.Endpoint == "/v1/catalog", appleEndpoints );
    }

    /// <summary>
    /// Verifies that different providers have isolated rate limits.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DifferentProviders_HaveIsolatedRateLimits( ) {
        // Arrange
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMinutes( 5 );

        await _tracker.SetRateLimitedAsync( SupportedProviders.Spotify, "/search", retryAfter, TestContext.CancellationToken );

        // Act
        RateLimitState spotifyState = await _tracker.GetStateAsync( SupportedProviders.Spotify, "/search", TestContext.CancellationToken );
        RateLimitState appleState = await _tracker.GetStateAsync( SupportedProviders.AppleMusic, "/search", TestContext.CancellationToken );
        RateLimitState tidalState = await _tracker.GetStateAsync( SupportedProviders.Tidal, "/search", TestContext.CancellationToken );

        // Assert - only Spotify should be rate limited
        Assert.IsTrue( spotifyState.IsRateLimited );
        Assert.IsFalse( appleState.IsRateLimited );
        Assert.IsFalse( tidalState.IsRateLimited );
    }

    /// <summary>
    /// Verifies that rate limits expire automatically based on TTL.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RateLimit_ExpiresAutomatically( ) {
        // Arrange - set a very short TTL
        DateTimeOffset retryAfter = DateTimeOffset.UtcNow.AddMilliseconds( 500 );

        await _tracker.SetRateLimitedAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            retryAfter,
            TestContext.CancellationToken
        );

        // Verify it's set
        RateLimitState stateBefore = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );
        Assert.IsTrue( stateBefore.IsRateLimited );

        // Wait for expiration
        await Task.Delay( 600, TestContext.CancellationToken );

        // Act
        RateLimitState stateAfter = await _tracker.GetStateAsync(
            SupportedProviders.Spotify,
            "/v1/search",
            TestContext.CancellationToken
        );

        // Assert - should be expired
        Assert.IsFalse( stateAfter.IsRateLimited );
    }
}
