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
        foreach (string pattern in new[] {
            "saga:*",
            "outbox:lookup-dispatch:*",
            "cache:refresh:pending:*",
            "queue:spotify:interactive",
            "queue:spotify:bulk",
            "queue:spotify:bulk:track-id",
            "queue:spotify:bulk:album-id",
            "queue:applemusic:interactive",
            "queue:tidal:interactive"
        }) {
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
        TaskCompletionSource<string> workSignal = new(
            TaskCreationOptions.RunContinuationsAsynchronously );
        ISubscriber subscriber = s_redis!.GetSubscriber( );
        RedisChannel workChannel = RedisChannel.Literal(
            QueueStreamKeys.WorkSignalFor( SupportedProviders.Spotify ) );
        await subscriber.SubscribeAsync(
            workChannel,
            (channel, value) => {
                _ = channel;
                _ = workSignal.TrySetResult( value.ToString( ) );
            } );
        Assert.AreEqual( 1, await _outbox.DispatchPendingAsync( 10, TestContext.CancellationToken ) );
        Assert.IsFalse( string.IsNullOrWhiteSpace( await workSignal.Task.WaitAsync(
            TimeSpan.FromSeconds( 2 ), TestContext.CancellationToken ) ) );
        await subscriber.UnsubscribeAsync( workChannel );
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

        RedisRequestQueue<QueuedLookupRequest> parsingQueue = new(
            s_redis,
            new Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>>( ).Object,
            _settings,
            SupportedProviders.Spotify );
        await parsingQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );
        QueuedMessage<QueuedLookupRequest>? parsedDelivery = await parsingQueue.DequeueAsync(
            TestContext.CancellationToken );
        Assert.IsNotNull( parsedDelivery );
        Assert.AreEqual( request.RequestId, parsedDelivery.Payload.RequestId );
        Assert.IsGreaterThan( DateTimeOffset.UtcNow.AddMinutes( -1 ), parsedDelivery.EnqueuedAt );

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

    /// <summary>A legacy incomplete provider leg is staged rather than assumed to have a delivery.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Stage_LegacyProviderLegWithoutDispatchState_StagesRecoveryDelivery( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-LEGACY" );
        Assert.IsTrue( await _sagas.TryInitializeProviderStatesAsync(
            saga.SagaId,
            [SupportedProviders.Spotify],
            saga.InstanceToken!,
            TestContext.CancellationToken ) );

        ProviderDispatchStageOutcome outcome = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );

        Assert.AreEqual( ProviderDispatchStageOutcome.Staged, outcome );
        Assert.IsTrue( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );
    }

    /// <summary>Batch staging exposes the complete provider set before any relay can process it.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task StageBatch_InitializesCompleteProviderSetAtomically( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-BATCH" );
        SupportedProviders[] providers = [
            SupportedProviders.Spotify,
            SupportedProviders.AppleMusic,
            SupportedProviders.Tidal
        ];
        QueuedLookupRequest[] requests = [.. providers.Select( provider =>
            CreateRequest( saga ) with { Provider = provider } )];

        IReadOnlyDictionary<SupportedProviders, ProviderDispatchStageOutcome> outcomes =
            await _outbox.StageBatchAsync(
                requests, QueuePriority.Interactive, TestContext.CancellationToken );
        LookupSagaState stagedSaga = (await _sagas.GetAsync(
            saga.SagaId, TestContext.CancellationToken ))!;

        Assert.HasCount( providers.Length, outcomes );
        Assert.IsTrue( outcomes.Values.All(
            outcome => outcome == ProviderDispatchStageOutcome.Staged ) );
        Assert.HasCount( providers.Length, stagedSaga.ProviderStates );
        Assert.IsTrue( stagedSaga.ProviderStates.Values.All( state => !state.IsComplete ) );
        Assert.AreEqual( providers.Length, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
    }

    /// <summary>Concurrent relays transfer one staged record to the provider stream exactly once.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DispatchAsync_ConcurrentRelays_PublishesExactlyOnce( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-CONCURRENT" );
        _ = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );

        bool[] results = await Task.WhenAll(
            _outbox.DispatchAsync(
                saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ),
            _outbox.DispatchAsync(
                saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );

        Assert.ContainsSingle( results.Where( result => result ) );
        Assert.HasCount( 1, await s_redis!.GetDatabase( ).StreamRangeAsync(
            QueueStreamKeys.For( SupportedProviders.Spotify, QueuePriority.Interactive ) ) );
    }

    /// <summary>A replaced saga generation cannot receive a delivery staged by the prior token.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DispatchAsync_WhenSagaTokenChanges_DropsStaleOutboxItem( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-TOKEN-REPLACED" );
        _ = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );
        await s_redis!.GetDatabase( ).HashSetAsync(
            $"saga:{saga.SagaId}", "instanceToken", "replacement-token" );

        Assert.IsFalse( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );
        Assert.IsEmpty( await s_redis.GetDatabase( ).StreamRangeAsync(
            QueueStreamKeys.For( SupportedProviders.Spotify, QueuePriority.Interactive ) ) );
    }

    /// <summary>A published leg that remains incomplete is autonomously re-driven after staleness.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DispatchPendingAsync_WhenPublishedLegIsStale_RedrivesLostDelivery( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-STALE-PUBLISHED" );
        _ = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );
        Assert.IsTrue( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );

        IDatabase db = s_redis!.GetDatabase( );
        RedisKey stream = QueueStreamKeys.For(
            SupportedProviders.Spotify, QueuePriority.Interactive );
        _ = await db.KeyDeleteAsync( stream );
        await db.HashSetAsync(
            $"saga:{saga.SagaId}:provider:{SupportedProviders.Spotify}",
            "dispatchPublishedAt",
            DateTimeOffset.UtcNow.AddMinutes( -6 ).ToUnixTimeMilliseconds( ) );
        _ = await db.SortedSetAddAsync(
            "outbox:lookup-dispatch:published-recovery-due",
            $"outbox:lookup-dispatch:{saga.SagaId}:{SupportedProviders.Spotify}",
            DateTimeOffset.UtcNow.AddMinutes( -1 ).ToUnixTimeMilliseconds( ) );

        Assert.AreEqual( 1, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
        Assert.HasCount( 1, await db.StreamRangeAsync( stream ) );
    }

    /// <summary>A live stream entry is not duplicated merely because its recovery check is due.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DispatchPendingAsync_WhenPublishedDeliveryStillExists_DoesNotDuplicateIt( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-LIVE-PUBLISHED" );
        _ = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );
        Assert.IsTrue( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );

        IDatabase db = s_redis!.GetDatabase( );
        RedisKey stream = QueueStreamKeys.For(
            SupportedProviders.Spotify, QueuePriority.Interactive );
        string outboxKey = $"outbox:lookup-dispatch:{saga.SagaId}:{SupportedProviders.Spotify}";
        _ = await db.SortedSetAddAsync(
            "outbox:lookup-dispatch:published-recovery-due",
            outboxKey,
            DateTimeOffset.UtcNow.AddMinutes( -1 ).ToUnixTimeMilliseconds( ) );

        Assert.AreEqual( 0, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
        Assert.HasCount( 1, await db.StreamRangeAsync( stream ) );
        double? nextCheck = await db.SortedSetScoreAsync(
            "outbox:lookup-dispatch:published-recovery-due", outboxKey );
        Assert.IsNotNull( nextCheck );
        Assert.IsGreaterThan( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds( ), nextCheck.Value );
    }

    /// <summary>A deferred replacement becomes the tracked delivery and suppresses redrive.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RequeueAsync_WithDeferral_UpdatesOutboxDeliveryLineage( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-DEFERRED" );
        _ = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );
        Assert.IsTrue( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );

        RedisRequestQueue<QueuedLookupRequest> queue = new(
            s_redis!,
            new Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>>( ).Object,
            _settings,
            SupportedProviders.Spotify );
        await queue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );
        QueuedMessage<QueuedLookupRequest>? delivery = await queue.DequeueAsync(
            TestContext.CancellationToken );
        Assert.IsNotNull( delivery );
        await queue.RequeueAsync(
            delivery.MessageId, TimeSpan.FromMinutes( 30 ), TestContext.CancellationToken );

        IDatabase db = s_redis!.GetDatabase( );
        RedisKey stream = QueueStreamKeys.For(
            SupportedProviders.Spotify, QueuePriority.Interactive );
        string outboxKey = $"outbox:lookup-dispatch:{saga.SagaId}:{SupportedProviders.Spotify}";
        _ = await db.SortedSetAddAsync(
            "outbox:lookup-dispatch:published-recovery-due",
            outboxKey,
            DateTimeOffset.UtcNow.AddMinutes( -1 ).ToUnixTimeMilliseconds( ) );

        Assert.AreEqual( 0, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
        StreamEntry[] entries = await db.StreamRangeAsync( stream );
        Assert.HasCount( 1, entries );
        QueuedLookupRequest? replacement = JsonSerializer.Deserialize<QueuedLookupRequest>(
            entries[0][QueueStreamFieldNames.Payload].ToString( ), s_jsonOptions );
        Assert.IsNotNull( replacement );
        Assert.IsNotNull( replacement.NotBefore );
        Assert.IsGreaterThan( DateTimeOffset.UtcNow.AddMinutes( 29 ), replacement.NotBefore.Value );
    }

    /// <summary>Refresh review context and provider legs are committed in one staging operation.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task StageRefreshBatch_AtomicallyRegistersReviewContextAndDispatches( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-REFRESH-CONTEXT" );
        const string SourceUri = "at://did:plc:test/link.bridgebeats.lookup/refresh-context";
        RefreshReviewEntry reviewEntry = new( ) {
            SourceRecordUri = SourceUri,
            SourceRecordCid = "bafyreifresh",
            SagaId = saga.SagaId,
            InstanceToken = saga.InstanceToken,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = saga.LookupValue
        };
        RedisRefreshReviewStore reviewStore = new(
            s_redis!,
            _settings,
            new Mock<ILogger<RedisRefreshReviewStore>>( ).Object );

        IReadOnlyDictionary<SupportedProviders, ProviderDispatchStageOutcome> outcomes =
            await _outbox.StageRefreshBatchAsync(
                [CreateRequest( saga )],
                QueuePriority.Bulk,
                reviewEntry,
                TestContext.CancellationToken );

        Assert.AreEqual(
            ProviderDispatchStageOutcome.Staged,
            outcomes[SupportedProviders.Spotify] );
        RefreshReviewEntry? stored = await reviewStore.GetPendingAsync(
            SourceUri, TestContext.CancellationToken );
        Assert.IsNotNull( stored );
        Assert.AreEqual( saga.InstanceToken, stored.InstanceToken );
        Assert.AreEqual( 1, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
    }

    /// <summary>A stale refresh generation writes neither review context nor provider work.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task StageRefreshBatch_WithStaleToken_WritesNothing( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-REFRESH-STALE" );
        const string SourceUri = "at://did:plc:test/link.bridgebeats.lookup/refresh-stale";
        QueuedLookupRequest request = CreateRequest( saga ) with {
            SagaInstanceToken = "stale-token"
        };
        RefreshReviewEntry reviewEntry = new( ) {
            SourceRecordUri = SourceUri,
            SagaId = saga.SagaId,
            InstanceToken = "stale-token",
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = saga.LookupValue
        };
        RedisRefreshReviewStore reviewStore = new(
            s_redis!,
            _settings,
            new Mock<ILogger<RedisRefreshReviewStore>>( ).Object );

        IReadOnlyDictionary<SupportedProviders, ProviderDispatchStageOutcome> outcomes =
            await _outbox.StageRefreshBatchAsync(
                [request], QueuePriority.Bulk, reviewEntry, TestContext.CancellationToken );

        Assert.AreEqual(
            ProviderDispatchStageOutcome.SagaInstanceMismatch,
            outcomes[SupportedProviders.Spotify] );
        Assert.IsNull( await reviewStore.GetPendingAsync(
            SourceUri, TestContext.CancellationToken ) );
        Assert.AreEqual( 0, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
    }

    /// <summary>Each relay batch reserves capacity for due recovery amid pending traffic.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DispatchPendingAsync_WithSustainedPendingWork_StillProcessesRecovery( ) {
        LookupSagaState lostSaga = await CreateSagaAsync( "OUTBOX-FAIR-LOST" );
        _ = await _outbox.StageAsync(
            CreateRequest( lostSaga ), QueuePriority.Interactive, TestContext.CancellationToken );
        Assert.IsTrue( await _outbox.DispatchAsync(
            lostSaga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );

        IDatabase db = s_redis!.GetDatabase( );
        RedisKey stream = QueueStreamKeys.For(
            SupportedProviders.Spotify, QueuePriority.Interactive );
        _ = await db.KeyDeleteAsync( stream );
        string lostOutboxKey =
            $"outbox:lookup-dispatch:{lostSaga.SagaId}:{SupportedProviders.Spotify}";
        _ = await db.SortedSetAddAsync(
            "outbox:lookup-dispatch:published-recovery-due",
            lostOutboxKey,
            DateTimeOffset.UtcNow.AddMinutes( -1 ).ToUnixTimeMilliseconds( ) );

        for (int index = 0; index < 4; index++) {
            LookupSagaState pendingSaga = await CreateSagaAsync( $"OUTBOX-FAIR-PENDING-{index}" );
            _ = await _outbox.StageAsync(
                CreateRequest( pendingSaga ), QueuePriority.Interactive, TestContext.CancellationToken );
        }

        Assert.AreEqual( 2, await _outbox.DispatchPendingAsync(
            2, TestContext.CancellationToken ) );
        Assert.HasCount( 2, await db.StreamRangeAsync( stream ) );
        Assert.AreEqual(
            3,
            await db.SortedSetLengthAsync( "outbox:lookup-dispatch:pending-due" ) );
    }

    /// <summary>A leg completed after staging is cleaned up without publishing stale work.</summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DispatchAsync_WhenLegCompletesAfterStage_CleansOutboxWithoutDelivery( ) {
        LookupSagaState saga = await CreateSagaAsync( "OUTBOX-COMPLETE-RACE" );
        _ = await _outbox.StageAsync(
            CreateRequest( saga ), QueuePriority.Interactive, TestContext.CancellationToken );
        Assert.IsTrue( await _sagas.TryUpdateProviderStateAsync(
            saga.SagaId,
            new ProviderLookupState(
                SupportedProviders.Spotify,
                IsComplete: true,
                IsSuccess: false,
                ResultJson: null,
                CompletedAt: DateTimeOffset.UtcNow,
                ErrorMessage: "completed before relay" ),
            saga.InstanceToken!,
            TestContext.CancellationToken ) );

        Assert.IsFalse( await _outbox.DispatchAsync(
            saga.SagaId, SupportedProviders.Spotify, TestContext.CancellationToken ) );
        Assert.AreEqual( 0, await _outbox.DispatchPendingAsync(
            10, TestContext.CancellationToken ) );
        Assert.IsEmpty( await s_redis!.GetDatabase( ).StreamRangeAsync(
            QueueStreamKeys.For( SupportedProviders.Spotify, QueuePriority.Interactive ) ) );
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
