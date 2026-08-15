using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>Integration coverage for atomic provider-leg staging and durable dispatch relay.</summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
[DoNotParallelize]
public class RedisLookupDispatchOutboxTests {
    private static IConnectionMultiplexer? s_redis;
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNameCaseInsensitive = true
    };
    private RedisSagaStateManager _sagas = null!;
    private RedisLookupDispatchOutbox _outbox = null!;
    private IOptions<QueueSettings> _settings = null!;

    /// <summary>The MSTest context supplying cancellation.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Starts the shared Redis fixture.</summary>
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

    /// <summary>Clears test keys and creates fresh Redis-backed collaborators.</summary>
    [TestInitialize]
    public async Task Initialize( ) {
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        foreach (string pattern in new[] { "saga:*", "outbox:lookup-dispatch:*", "queue:spotify:interactive" }) {
            await foreach (RedisKey key in server.KeysAsync( pattern: pattern )) {
                _ = await db.KeyDeleteAsync( key );
            }
        }

        _settings = Options.Create( new QueueSettings { JobExpirationMinutes = 60 } );
        _sagas = new RedisSagaStateManager(
            s_redis,
            new Mock<ILogger<RedisSagaStateManager>>( ).Object,
            _settings );
        _outbox = new RedisLookupDispatchOutbox(
            s_redis,
            _settings,
            new Mock<ILogger<RedisLookupDispatchOutbox>>( ).Object );
    }

    /// <summary>Staging creates provider state and a relay can publish it exactly once.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task StageThenRelay_PublishesExactlyOneProviderDelivery( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-EXACTLY-ONCE" );
        QueuedLookupRequest request = CreateRequest( saga );

        ProviderDispatchStageOutcome staged = await _outbox.StageAsync(
            request, QueuePriority.Interactive, TestContext.CancellationToken );
        LookupSagaState afterStage = (await _sagas.GetAsync(
            saga.SagaId, TestContext.CancellationToken ))!;

        Assert.AreEqual( ProviderDispatchStageOutcome.Staged, staged );
        Assert.IsTrue( afterStage.ProviderStates.ContainsKey( SupportedProviders.Spotify ) );
        Assert.AreEqual( 1, await _outbox.DispatchPendingAsync( 10, TestContext.CancellationToken ) );
        Assert.AreEqual( 0, await _outbox.DispatchPendingAsync( 10, TestContext.CancellationToken ) );

        IDatabase db = s_redis!.GetDatabase( );
        StreamEntry[] entries = await db.StreamRangeAsync(
            QueueStreamKeys.For( SupportedProviders.Spotify, QueuePriority.Interactive ) );
        Assert.HasCount( 1, entries );
        QueuedLookupRequest? published = JsonSerializer.Deserialize<QueuedLookupRequest>(
            entries[0][QueueStreamFieldNames.Payload].ToString( ),
            s_jsonOptions );
        Assert.IsNotNull( published );
        Assert.AreEqual( request.RequestId, published.RequestId );

        ProviderDispatchStageOutcome repeated = await _outbox.StageAsync(
            request with { RequestId = Guid.NewGuid( ).ToString( "N" ) },
            QueuePriority.Interactive,
            TestContext.CancellationToken );
        Assert.AreEqual( ProviderDispatchStageOutcome.AlreadyDispatched, repeated );
        Assert.IsFalse( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );
        Assert.HasCount( 1, await db.StreamRangeAsync(
            QueueStreamKeys.For( SupportedProviders.Spotify, QueuePriority.Interactive ) ) );
    }

    /// <summary>A stale saga generation cannot stage an outbox entry or initialize provider state.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Stage_WithStaleInstanceToken_DoesNotCreateWork( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-STALE" );
        QueuedLookupRequest request = CreateRequest( saga ) with { SagaInstanceToken = "stale-token" };

        ProviderDispatchStageOutcome outcome = await _outbox.StageAsync(
            request, QueuePriority.Interactive, TestContext.CancellationToken );

        Assert.AreEqual( ProviderDispatchStageOutcome.SagaInstanceMismatch, outcome );
        LookupSagaState current = (await _sagas.GetAsync(
            saga.SagaId, TestContext.CancellationToken ))!;
        Assert.IsEmpty( current.ProviderStates );
        Assert.AreEqual( 0, await _outbox.DispatchPendingAsync( 10, TestContext.CancellationToken ) );
    }

    private async Task<LookupSagaState> CreateSagaAsync( string value ) {
        string lookupKey = $"IsrcLookup:{value}";
        string sagaId = ISagaStateManager.GenerateSagaId( lookupKey );
        return await _sagas.GetOrCreateAsync(
            sagaId,
            lookupKey,
            LookupRequestType.IsrcLookup,
            value,
            QueuePriority.Interactive,
            TestContext.CancellationToken );
    }

    private static QueuedLookupRequest CreateRequest( LookupSagaState saga ) => new( ) {
        RequestId = Guid.NewGuid( ).ToString( "N" ),
        Provider = SupportedProviders.Spotify,
        LookupType = LookupRequestType.IsrcLookup,
        LookupValue = saga.LookupValue,
        SagaId = saga.SagaId,
        SagaInstanceToken = saga.InstanceToken,
        OriginPriority = QueuePriority.Interactive
    };
}
