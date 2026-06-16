using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for the stale-cache refresh sweep's saga-state interaction against a real Redis
/// instance (the shared Testcontainers Redis). Verifies that the first-writer-wins rule is honoured
/// when a saga already exists at a higher priority: a bulk-origin backfill targeting the same
/// <c>lookupKey</c> must return the existing saga without overwriting the origin priority.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public class StaleCacheRefreshIntegrationTests {

    /// <summary>The shared Redis connection used for these tests.</summary>
    private static IConnectionMultiplexer? s_redis;

    /// <summary>Logger mock injected into the saga manager.</summary>
    private Mock<ILogger<RedisSagaStateManager>> _loggerMock = null!;
    /// <summary>Queue settings supplied to the saga manager.</summary>
    private IOptions<QueueSettings> _queueSettings = null!;
    /// <summary>The saga state manager under test.</summary>
    private RedisSagaStateManager _sagaManager = null!;

    /// <summary>MSTest-injected test context.</summary>
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

    /// <summary>Closes and disposes the Redis connection after the class completes.</summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>Clears saga keys and builds a fresh saga manager before each test.</summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "saga:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _loggerMock = new Mock<ILogger<RedisSagaStateManager>>( );
        _queueSettings = Options.Create( new QueueSettings { JobExpirationMinutes = 60 } );
        _sagaManager = new RedisSagaStateManager( s_redis, _loggerMock.Object, _queueSettings );
    }

    /// <summary>
    /// When a saga for lookup key K already exists at <c>QueuePriority.Interactive</c> origin and the
    /// backfill sweep calls <c>GetOrCreateAsync</c> for the same K at <c>QueuePriority.Bulk</c>,
    /// the call must return the existing saga (first-writer-wins: the origin priority stays Interactive)
    /// and no duplicate saga is created.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task GetOrCreateAsync_ExistingInteractiveSaga_BackfillDoesNotOverwriteOrigin( ) {
        const string LookupKey = "IsrcLookup:TESTISRC_INTEGRATION_001";
        string sagaId = ISagaStateManager.GenerateSagaId( LookupKey );

        // Create the saga at Interactive origin (simulates a prior user-triggered lookup)
        LookupSagaState interactiveSaga = await _sagaManager.GetOrCreateAsync(
            sagaId,
            LookupKey,
            LookupRequestType.IsrcLookup,
            "TESTISRC_INTEGRATION_001",
            originPriority: QueuePriority.Interactive,
            cancellationToken: TestContext.CancellationToken
        );

        Assert.AreEqual( QueuePriority.Interactive, interactiveSaga.OriginPriority,
            "Pre-condition: saga must have been created with Interactive origin" );

        // Simulate the backfill sweep calling GetOrCreateAsync for the same key at Bulk origin
        LookupSagaState backfillResult = await _sagaManager.GetOrCreateAsync(
            sagaId,
            LookupKey,
            LookupRequestType.IsrcLookup,
            "TESTISRC_INTEGRATION_001",
            originPriority: QueuePriority.Bulk,
            cancellationToken: TestContext.CancellationToken
        );

        // First-writer-wins: origin priority must remain Interactive, not be downgraded to Bulk
        Assert.AreEqual( QueuePriority.Interactive, backfillResult.OriginPriority,
            "First-writer-wins: the existing Interactive origin must not be overwritten by the Bulk backfill" );

        // The returned saga must be the same one (same sagaId, same lookupKey)
        Assert.AreEqual( sagaId, backfillResult.SagaId );
        Assert.AreEqual( LookupKey, backfillResult.LookupKey );

        // No duplicate saga exists — reading the saga by id returns the original
        LookupSagaState? readBack = await _sagaManager.GetAsync( sagaId, TestContext.CancellationToken );
        Assert.IsNotNull( readBack, "The saga must still exist in Redis" );
        Assert.AreEqual( QueuePriority.Interactive, readBack.OriginPriority,
            "Reading the saga back must confirm Interactive origin was preserved" );
    }
}
