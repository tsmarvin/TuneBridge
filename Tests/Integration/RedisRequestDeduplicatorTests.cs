using BridgeBeats.Infrastructure.Queue;
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
