using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisRequestDeduplicator"/> using the shared Redis container.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class RedisRequestDeduplicatorTests {

    private static IConnectionMultiplexer? s_redis;

    private Mock<ILogger<RedisRequestDeduplicator>> _mockLogger = null!;
    private RedisRequestDeduplicator _deduplicator = null!;

    /// <summary>
    /// Initializes the Redis connection for all tests in the class.
    /// </summary>
    /// <param name="_">The test context provided by the test framework (unused).</param>
    [ClassInitialize]
    public static async Task ClassInitialize( TestContext _ ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Closes and disposes the Redis connection after all tests complete.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears inflight-related Redis keys and initializes the deduplicator before each test.
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
    /// Verifies that <see cref="RedisRequestDeduplicator.TryAcquireAsync"/> returns Acquired=true
    /// when no other request is in flight for the same key.
    /// </summary>
    [TestMethod]
    public async Task TryAcquireAsync_ReturnsAcquired_WhenNotInFlight( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // Act
        Contracts.Records.DeduplicationResult result = await _deduplicator.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 )
        );

        // Assert
        Assert.IsTrue( result.Acquired );
        Assert.IsFalse( result.AlreadyInFlight );
        Assert.AreEqual( requestKey, result.RequestKey );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestDeduplicator.TryAcquireAsync"/> returns AlreadyInFlight=true
    /// when another instance already holds the lock for the same key.
    /// </summary>
    [TestMethod]
    public async Task TryAcquireAsync_ReturnsAlreadyInFlight_WhenLockHeld( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";


        // First acquisition
        Contracts.Records.DeduplicationResult firstResult = await _deduplicator.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 )
        );
        Assert.IsTrue( firstResult.Acquired );

        // Create second deduplicator (simulates different instance)
        Mock<ILogger<RedisRequestDeduplicator>> logger2 = new( );
        RedisRequestDeduplicator deduplicator2 = new( s_redis!, logger2.Object );

        // Act - second acquisition should fail
        Contracts.Records.DeduplicationResult secondResult = await deduplicator2.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 )
        );

        // Assert
        Assert.IsFalse( secondResult.Acquired );
        Assert.IsTrue( secondResult.AlreadyInFlight );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestDeduplicator.ReleaseAsync"/> removes the lock,
    /// allowing a new acquisition of the same key.
    /// </summary>
    [TestMethod]
    public async Task ReleaseAsync_RemovesLock( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";
        string resultUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";

        _ = await _deduplicator.TryAcquireAsync( requestKey, TimeSpan.FromMinutes( 5 ) );

        // Act
        await _deduplicator.ReleaseAsync( requestKey, resultUri );

        // Assert - lock should be released, can acquire again
        Contracts.Records.DeduplicationResult result = await _deduplicator.TryAcquireAsync(
            requestKey,
            TimeSpan.FromMinutes( 5 )
        );
        Assert.IsTrue( result.Acquired );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestDeduplicator.WaitForCompletionAsync"/> returns null
    /// when no lock is held (request already completed).
    /// </summary>
    [TestMethod]
    public async Task WaitForCompletionAsync_ReturnsNull_WhenAlreadyCompleted( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // No lock held = already completed

        // Act
        string? result = await _deduplicator.WaitForCompletionAsync(
            requestKey,
            TimeSpan.FromSeconds( 1 )
        );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestDeduplicator.WaitForCompletionAsync"/> returns null
    /// when the wait times out before the result is published.
    /// </summary>
    [TestMethod]
    public async Task WaitForCompletionAsync_ReturnsNull_OnTimeout( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";

        // Acquire lock but don't release
        _ = await _deduplicator.TryAcquireAsync( requestKey, TimeSpan.FromMinutes( 5 ) );

        // Create second deduplicator to wait
        Mock<ILogger<RedisRequestDeduplicator>> logger2 = new( );
        RedisRequestDeduplicator deduplicator2 = new( s_redis!, logger2.Object );

        // Act - wait with short timeout
        string? result = await deduplicator2.WaitForCompletionAsync(
            requestKey,
            TimeSpan.FromMilliseconds( 100 )
        );

        // Assert
        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestDeduplicator.WaitForCompletionAsync"/> receives the
    /// result URI when it is published via <see cref="RedisRequestDeduplicator.ReleaseAsync"/>.
    /// </summary>
    [TestMethod]
    public async Task WaitForCompletionAsync_ReceivesResult_WhenPublished( ) {
        // Arrange
        string requestKey = "isrc:USRC12345678";
        string expectedUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678";

        _ = await _deduplicator.TryAcquireAsync( requestKey, TimeSpan.FromMinutes( 5 ) );

        // Create second deduplicator to wait
        Mock<ILogger<RedisRequestDeduplicator>> logger2 = new( );
        RedisRequestDeduplicator deduplicator2 = new( s_redis!, logger2.Object );

        // Start waiting in background
        Task<string?> waitTask = deduplicator2.WaitForCompletionAsync(
            requestKey,
            TimeSpan.FromSeconds( 5 )
        );

        // Give subscription time to establish
        await Task.Delay( 100 );

        // Act - release with result
        await _deduplicator.ReleaseAsync( requestKey, expectedUri );

        // Assert
        string? result = await waitTask;
        Assert.AreEqual( expectedUri, result );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestDeduplicator.GenerateRequestKey"/> creates consistent,
    /// normalized keys regardless of input casing or whitespace.
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
