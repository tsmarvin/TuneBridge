using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Cache;
using BridgeBeats.Core.Infrastructure.Storage;
using BridgeBeats.Core.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisMediaLinkCache"/> using the shared Redis container.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class RedisMediaLinkCacheTests {

    private static IConnectionMultiplexer? _redis;

    private Mock<IATProtoStorageService> _mockAtProto = null!;
    private Mock<ILogger<RedisMediaLinkCache>> _mockLogger = null!;
    private RedisMediaLinkCache _cache = null!;

    private const int CacheDays = 7;
    private const string UserDID = "did:plc:testuser123";

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
        _redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Cleans up the Redis connection after all tests in this class have completed.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (_redis is not null) {
            await _redis.CloseAsync( );
            _redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears cache-related keys and creates a fresh cache instance before each test.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        // Clear only cache-related keys before each test
        IDatabase db = _redis!.GetDatabase( );
        IServer server = _redis.GetServer( _redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "lookup:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _mockAtProto = new Mock<IATProtoStorageService>( );
        _mockLogger = new Mock<ILogger<RedisMediaLinkCache>>( );

        _cache = new RedisMediaLinkCache(
            _redis,
            _mockAtProto.Object,
            _mockLogger.Object,
            CacheDays,
            UserDID
        );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.CacheResultAsync"/> stores lookup keys in Redis.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task CacheResultAsync_StoresLookupKeys_InRedis( ) {
        // Arrange
        MediaLinkResult result = CreateTestResult( "USRC12345678", false );
        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:USRC12345678";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        // Act
        string cachedUri = await _cache.CacheResultAsync( result );

        // Assert
        Assert.AreEqual( recordUri, cachedUri );

        // Verify ISRC key exists
        IDatabase db = _redis!.GetDatabase( );
        RedisValue isrcValue = await db.StringGetAsync( "lookup:isrc:USRC12345678" );
        Assert.IsFalse( isrcValue.IsNullOrEmpty );
        Assert.AreEqual( recordUri, isrcValue.ToString( ) );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.TryGetCachedResultByISRCAsync"/> returns the cached result.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryGetCachedResultByISRCAsync_ReturnsResult_WhenCached( ) {
        // Arrange
        MediaLinkResult result = CreateTestResult( "ISRC999888777", false );
        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:ISRC999888777";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        _ = await _cache.CacheResultAsync( result );

        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultByISRCAsync( "ISRC999888777" );

        // Assert
        Assert.IsNotNull( lookupResult );
        Assert.AreEqual( recordUri, lookupResult.Value.cachedUri );
        Assert.IsFalse( lookupResult.Value.isStale );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.TryGetCachedResultByUPCAsync"/> returns the cached result.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryGetCachedResultByUPCAsync_ReturnsResult_WhenCached( ) {
        // Arrange
        MediaLinkResult result = CreateTestResult( "123456789012", true );
        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/album:123456789012";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        _ = await _cache.CacheResultAsync( result );

        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultByUPCAsync( "123456789012" );

        // Assert
        Assert.IsNotNull( lookupResult );
        Assert.AreEqual( recordUri, lookupResult.Value.cachedUri );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.TryGetCachedResultAsync"/> returns the cached result by URL.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryGetCachedResultAsync_ReturnsResult_WhenUrlCached( ) {
        // Arrange
        string inputUrl = "https://open.spotify.com/track/abc123";
        MediaLinkResult result = CreateTestResult( "TESTISRC001", false );
        result._inputLinks.Add( inputUrl );

        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:TESTISRC001";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        _ = await _cache.CacheResultAsync( result );

        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultAsync( inputUrl );

        // Assert
        Assert.IsNotNull( lookupResult );
        Assert.AreEqual( recordUri, lookupResult.Value.cachedUri );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.TryGetCachedResultByCardIdAsync"/> returns the cached result by card ID.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryGetCachedResultByCardIdAsync_ReturnsResult_WhenCached( ) {
        // Arrange
        MediaLinkResult result = CreateTestResult( "CARDTEST123", false );
        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:CARDTEST123";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        _ = await _cache.CacheResultAsync( result );

        // Get the card ID from the result
        string rkey = RecordKeyGenerator.GenerateRkey( result );
        string cardId = RecordKeyGenerator.GenerateCardId( rkey );

        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultByCardIdAsync( cardId );

        // Assert
        Assert.IsNotNull( lookupResult );
        Assert.AreEqual( recordUri, lookupResult.Value.cachedUri );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.TryGetCachedResultByProviderIdAsync"/> returns the cached result by provider ID.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryGetCachedResultByProviderIdAsync_ReturnsResult_WhenCached( ) {
        // Arrange
        MediaLinkResult result = CreateTestResult( "PROVIDERTEST", false );
        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:PROVIDERTEST";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        _ = await _cache.CacheResultAsync( result );

        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultByProviderIdAsync( "abc123", SupportedProviders.Spotify, false );

        // Assert
        Assert.IsNotNull( lookupResult );
        Assert.AreEqual( recordUri, lookupResult.Value.cachedUri );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.CacheResultAsync"/> cleans up old URL keys when refreshing a cached result.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task CacheResultAsync_CleansUpOldKeys_OnRefresh( ) {
        // Arrange
        MediaLinkResult result1 = CreateTestResult( "REFRESHTEST1", false );
        result1._inputLinks.Add( "https://old.url/track1" );

        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:REFRESHTEST1";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result1 );

        _ = await _cache.CacheResultAsync( result1 );

        // Verify old URL is cached
        IDatabase db = _redis!.GetDatabase( );
        string oldUrlHash = HashUtility.HashUrl( "https://old.url/track1" );
        RedisValue oldValue = await db.StringGetAsync( $"lookup:url:{oldUrlHash}" );
        Assert.IsFalse( oldValue.IsNullOrEmpty, "Old URL should be cached" );

        // Now cache the same result with a different URL (simulating refresh)
        MediaLinkResult result2 = CreateTestResult( "REFRESHTEST1", false );
        result2._inputLinks.Add( "https://new.url/track1" );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result2 );

        _ = await _cache.CacheResultAsync( result2 );

        // Assert: Old URL key should be removed
        RedisValue oldValueAfterRefresh = await db.StringGetAsync( $"lookup:url:{oldUrlHash}" );
        Assert.IsTrue( oldValueAfterRefresh.IsNullOrEmpty, "Old URL key should be cleaned up on refresh" );

        // Assert: New URL key should exist
        string newUrlHash = HashUtility.HashUrl( "https://new.url/track1" );
        RedisValue newValue = await db.StringGetAsync( $"lookup:url:{newUrlHash}" );
        Assert.IsFalse( newValue.IsNullOrEmpty, "New URL should be cached" );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.TryGetCachedResultByISRCAsync"/> returns null when the ISRC is not cached.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TryGetCachedResultByISRCAsync_ReturnsNull_WhenNotCached( ) {
        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultByISRCAsync( "NONEXISTENT123" );

        // Assert
        Assert.IsNull( lookupResult );
    }

    /// <summary>
    /// Verifies that the cache marks results as stale when they are older than the configured cache duration.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task CheckRecordFreshness_ReturnsStale_WhenOlderThanCacheDays( ) {
        // Arrange
        // Create a result with LookedUpAt set to 8 days ago (older than CacheDays=7)
        MediaLinkResult result = CreateTestResultWithDate( "STALETEST123", false, DateTime.UtcNow.AddDays( -8 ) );

        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:STALETEST123";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        _ = await _cache.CacheResultAsync( result );

        // Act
        (MediaLinkResult cachedResult, string cachedUri, bool isStale)? lookupResult =
            await _cache.TryGetCachedResultByISRCAsync( "STALETEST123" );

        // Assert
        Assert.IsNotNull( lookupResult );
        Assert.IsTrue( lookupResult.Value.isStale, "Result should be marked as stale" );
    }

    /// <summary>
    /// Verifies that <see cref="RedisMediaLinkCache.AddInputLinksAsync"/> skips writing when the record
    /// already exists with the same RecordUri, avoiding unnecessary Redis writes during cache bootstrap.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task AddInputLinksAsync_SkipsWrite_WhenRecordAlreadyExistsWithSameUri( ) {
        // Arrange
        MediaLinkResult result = CreateTestResult( "SKIPTEST123", false );
        string recordUri = $"at://{UserDID}/link.bridgebeats.lookup/track:SKIPTEST123";

        _ = _mockAtProto
            .Setup( s => s.StoreMediaLinkResultAsync( It.IsAny<MediaLinkResult>( ) ) )
            .ReturnsAsync( recordUri );

        _ = _mockAtProto
            .Setup( s => s.GetMediaLinkResultAsync( recordUri ) )
            .ReturnsAsync( result );

        // First call - should write to Redis
        _ = await _cache.CacheResultAsync( result );

        // Get the ISRC key value and TTL after first write
        IDatabase db = _redis!.GetDatabase( );
        TimeSpan? ttlBefore = await db.KeyTimeToLiveAsync( "lookup:isrc:SKIPTEST123" );
        Assert.IsNotNull( ttlBefore, "ISRC key should exist after first write" );

        // Wait a brief moment to ensure TTL would be different if re-written
        await Task.Delay( 100, TestContext.CancellationToken );

        // Second call with same result and recordUri - should skip write but refresh TTL
        await _cache.AddInputLinksAsync( recordUri, result );

        // Get TTL after second call
        TimeSpan? ttlAfter = await db.KeyTimeToLiveAsync( "lookup:isrc:SKIPTEST123" );
        Assert.IsNotNull( ttlAfter, "ISRC key should still exist after skip" );

        // TTL should be refreshed (close to original cache days)
        // The TTL after should be >= TTL before (since we refreshed it)
        Assert.IsTrue( ttlAfter >= ttlBefore!.Value.Subtract( TimeSpan.FromSeconds( 1 ) ),
            "TTL should be refreshed, not reduced" );
    }

    /// <summary>
    /// Creates a test MediaLinkResult with the specified external ID.
    /// </summary>
    private static MediaLinkResult CreateTestResult( string externalId, bool isAlbum )
        => CreateTestResultWithDate( externalId, isAlbum, DateTime.UtcNow );

    /// <summary>
    /// Creates a test MediaLinkResult with the specified external ID and LookedUpAt date.
    /// </summary>
    private static MediaLinkResult CreateTestResultWithDate( string externalId, bool isAlbum, DateTime lookedUpAt ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = lookedUpAt
        };

        result.Results[SupportedProviders.Spotify] = new MusicLookupResult {
            Artist = "Test Artist",
            Title = isAlbum ? "Test Album" : "Test Track",
            ExternalId = externalId,
            URL = $"https://open.spotify.com/{(isAlbum ? "album" : "track")}/abc123",
            ArtUrl = "https://example.com/art.jpg",
            IsAlbum = isAlbum
        };

        return result;
    }
}
