using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>Real-Redis coverage for refresh-review promotion, TTL, and revision-guarded cleanup.</summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
[DoNotParallelize]
public sealed class RedisRefreshReviewStoreIntegrationTests {
    private const string SagaId = "refresh-review-integration-saga";
    private const string SourceUri = "at://did:plc:test/link.bridgebeats.lookup/track:INTEGRATION";
    private const string SourceCid = "bafyreihash";
    private static IConnectionMultiplexer? s_redis;
    private RedisRefreshReviewStore _store = null!;

    /// <summary>MSTest cancellation token for Redis operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Connects to the shared Testcontainers Redis instance.</summary>
    [ClassInitialize]
    public static async Task ClassInitialize( TestContext _ ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>Closes the shared Redis connection.</summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>Clears refresh-review keys and creates the store before each test.</summary>
    [TestInitialize]
    public async Task Initialize( ) {
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "cache:refresh:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _store = new RedisRefreshReviewStore(
            s_redis,
            Options.Create( new QueueSettings { JobExpirationMinutes = 60 } ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );
    }

    /// <summary>Promotion is durable and cleanup removes only the matching displayed revision.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task MarkAndDelete_RoundTripsWithRevisionCas( ) {
        RefreshReviewEntry entry = CreateEntry( );
        await _store.RegisterPendingAsync( entry, TestContext.CancellationToken );

        TimeSpan? ttl = await s_redis!.GetDatabase( ).KeyTimeToLiveAsync(
            $"cache:refresh:pending:{SagaId}" );
        Assert.IsNotNull( ttl );
        Assert.IsGreaterThan( TimeSpan.Zero, ttl.Value );
        Assert.IsLessThanOrEqualTo( TimeSpan.FromMinutes( 60 ), ttl.Value );

        await _store.MarkUnresolvedAsync(
            SagaId, "No provider result.", TestContext.CancellationToken );
        Assert.HasCount( 1, await _store.GetUnresolvedAsync( TestContext.CancellationToken ) );

        bool staleDelete = await _store.DeleteUnresolvedAsync(
            SourceUri, "stale-saga", "bafyreistale", TestContext.CancellationToken );
        Assert.IsFalse( staleDelete );
        Assert.HasCount( 1, await _store.GetUnresolvedAsync( TestContext.CancellationToken ) );

        bool matchingDelete = await _store.DeleteUnresolvedAsync(
            SourceUri, SagaId, SourceCid, TestContext.CancellationToken );
        Assert.IsTrue( matchingDelete );
        Assert.IsEmpty( await _store.GetUnresolvedAsync( TestContext.CancellationToken ) );
    }

    /// <summary>A successful completion removes pending context before it can become reviewable.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Complete_PendingEntry_RemovesContext( ) {
        await _store.RegisterPendingAsync( CreateEntry( ), TestContext.CancellationToken );

        await _store.CompleteAsync( SagaId, TestContext.CancellationToken );
        await _store.MarkUnresolvedAsync(
            SagaId, "late failure", TestContext.CancellationToken );

        Assert.IsEmpty( await _store.GetUnresolvedAsync( TestContext.CancellationToken ) );
    }

    private static RefreshReviewEntry CreateEntry( ) => new( ) {
        SourceRecordUri = SourceUri,
        SourceRecordCid = SourceCid,
        SagaId = SagaId,
        LookupType = LookupRequestType.IsrcLookup,
        LookupValue = "INTEGRATION"
    };
}
