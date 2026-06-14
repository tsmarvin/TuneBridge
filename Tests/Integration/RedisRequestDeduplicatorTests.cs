using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisRequestDeduplicator"/> against a real Redis instance (the
/// shared Testcontainers Redis). Verifies in-flight lock acquisition and contention, lock release,
/// completion waiting (immediate, on timeout, and on a published result via pub/sub), and the
/// canonicalization performed by the request-key generator. Requires Docker to be running on the host
/// machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class RedisRequestDeduplicatorTests {

    /// <summary>The shared Redis connection used by the deduplicator under test.</summary>
    private static IConnectionMultiplexer? s_redis;

    /// <summary>Mock logger captured for the deduplicator under test.</summary>
    private Mock<ILogger<RedisRequestDeduplicator>> _mockLogger = null!;
    /// <summary>The deduplicator under test, recreated for each test.</summary>
    private RedisRequestDeduplicator _deduplicator = null!;

    /// <summary>The MSTest-injected test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Requires the shared Redis container and opens a connection to it for the test class.
    /// </summary>
    /// <param name="_">The MSTest class context (unused).</param>
    [ClassInitialize]
    public static async Task ClassInitialize( TestContext _ ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Closes and disposes the Redis connection after the class completes.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears leftover <c>inflight:*</c> keys and constructs a fresh deduplicator before each test.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        // Clear only inflight-related keys before each test
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "inflight:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _mockLogger = new Mock<ILogger<RedisRequestDeduplicator>>( );

        _deduplicator = new RedisRequestDeduplicator(
            s_redis,
            _mockLogger.Object
        );
    }

    /// <summary>
    /// Verifies acquiring an in-flight lock for a key that is not yet in flight succeeds and reports the
    /// key as not already in flight.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryAcquireAsync_ReturnsAcquired_WhenNotInFlight( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // Act
        Contracts.Records.DeduplicationResult result = await _deduplicator.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 ),
            TestContext.CancellationToken
        );

        // Assert
        Assert.IsTrue( result.Acquired );
        Assert.IsFalse( result.AlreadyInFlight );
        Assert.AreEqual( requestKey, result.RequestKey );
    }

    /// <summary>
    /// Verifies a second acquirer fails to acquire the lock and reports the key as already in flight when
    /// the lock is held.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryAcquireAsync_ReturnsAlreadyInFlight_WhenLockHeld( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // First acquisition
        Contracts.Records.DeduplicationResult firstResult = await _deduplicator.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 ),
            TestContext.CancellationToken
        );
        Assert.IsTrue( firstResult.Acquired );

        // Create second deduplicator (simulates different instance)
        Mock<ILogger<RedisRequestDeduplicator>> logger2 = new( );
        RedisRequestDeduplicator deduplicator2 = new( s_redis!, logger2.Object );

        // Act - second acquisition should fail
        Contracts.Records.DeduplicationResult secondResult = await deduplicator2.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 ),
            TestContext.CancellationToken
        );

        // Assert
        Assert.IsFalse( secondResult.Acquired );
        Assert.IsTrue( secondResult.AlreadyInFlight );
    }

    /// <summary>
    /// Verifies releasing a lock removes it, allowing a subsequent acquisition to succeed.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ReleaseAsync_RemovesLock( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";
        string resultUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";

        _ = await _deduplicator.TryAcquireAsync( requestKey, TimeSpan.FromMinutes( 5 ), TestContext.CancellationToken );

        // Act
        await _deduplicator.ReleaseAsync( requestKey, resultUri, TestContext.CancellationToken );

        // Assert - lock should be released, can acquire again
        Contracts.Records.DeduplicationResult result = await _deduplicator.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 ),
            TestContext.CancellationToken
        );
        Assert.IsTrue( result.Acquired );
    }

    /// <summary>
    /// Verifies waiting for completion of a key that was never in flight returns null immediately.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task WaitForCompletionAsync_ReturnsNull_WhenAlreadyCompleted( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // No lock held = already completed

        // Act
        string? result = await _deduplicator.WaitForCompletionAsync(
            requestKey,
            TimeSpan.FromSeconds( 1 ),
            TestContext.CancellationToken
        );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies waiting for completion of a key that stays in flight returns null when the wait times
    /// out.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task WaitForCompletionAsync_ReturnsNull_OnTimeout( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // Acquire lock but don't release
        _ = await _deduplicator.TryAcquireAsync( requestKey, TimeSpan.FromMinutes( 5 ), TestContext.CancellationToken );

        // Create second deduplicator to wait
        Mock<ILogger<RedisRequestDeduplicator>> logger2 = new( );
        RedisRequestDeduplicator deduplicator2 = new( s_redis!, logger2.Object );

        // Act - wait with short timeout
        string? result = await deduplicator2.WaitForCompletionAsync(
            requestKey,
            TimeSpan.FromMilliseconds( 100 ),
            TestContext.CancellationToken
        );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies a waiter receives the result URI when the holder releases the lock and publishes the
    /// completion while the waiter is blocked.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task WaitForCompletionAsync_ReceivesResult_WhenPublished( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";
        string expectedUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";

        _ = await _deduplicator.TryAcquireAsync( requestKey, TimeSpan.FromMinutes( 5 ), TestContext.CancellationToken );

        // Create second deduplicator to wait
        Mock<ILogger<RedisRequestDeduplicator>> logger2 = new( );
        RedisRequestDeduplicator deduplicator2 = new( s_redis!, logger2.Object );

        // Start waiting in background
        Task<string?> waitTask = deduplicator2.WaitForCompletionAsync(
            requestKey,
            TimeSpan.FromSeconds( 5 ),
            TestContext.CancellationToken
        );

        // Give subscription time to establish
        await Task.Delay( 100, TestContext.CancellationToken );

        // Act - release with result
        await _deduplicator.ReleaseAsync( requestKey, expectedUri, TestContext.CancellationToken );

        // Assert
        string? result = await waitTask;
        Assert.AreEqual( expectedUri, result );
    }

    /// <summary>
    /// Verifies the request-key generator produces consistent, normalized keys regardless of input
    /// casing or whitespace.
    /// </summary>
    [TestMethod]
    public void GenerateRequestKey_CreatesConsistentKey( ) {
        // Act
        string key1 = RedisRequestDeduplicator.GenerateRequestKey( "isrc", "usrc12345678" );
        string key2 = RedisRequestDeduplicator.GenerateRequestKey( "ISRC", "USRC12345678" );
        string key3 = RedisRequestDeduplicator.GenerateRequestKey( "Isrc", "  UsRc12345678  " );

        // Assert - all should normalize to same key
        Assert.AreEqual( key1, key2 );
        Assert.AreEqual( key2, key3 );
        Assert.AreEqual( "isrc:USRC12345678", key1 );
    }
}
