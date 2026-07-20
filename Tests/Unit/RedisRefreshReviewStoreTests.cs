using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>Tests pending-to-unresolved promotion in the Redis refresh review store.</summary>
[TestClass]
public class RedisRefreshReviewStoreTests {
    private static readonly JsonSerializerOptions s_jsonOptions = new( JsonSerializerDefaults.Web );

    /// <summary>MSTest cancellation token for asynchronous store operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Promotion writes the durable review entry before deleting pending saga context.</summary>
    [TestMethod]
    public async Task MarkUnresolved_PendingEntry_WritesReviewThenDeletesPending( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );

        RefreshReviewEntry pending = CreateEntry( );
        _ = database.Setup( db => db.StringGetAsync(
                "cache:refresh:pending:refresh-saga", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( JsonSerializer.Serialize( pending, s_jsonOptions ) );

        int sequence = 0;
        int reviewWriteOrder = 0;
        int pendingDeleteOrder = 0;
        RedisValue reviewJson = RedisValue.Null;
        _ = database.Setup( db => db.HashSetAsync(
                "cache:refresh:unresolved",
                pending.SourceRecordUri,
                It.IsAny<RedisValue>( ),
                It.IsAny<When>( ),
                It.IsAny<CommandFlags>( ) ) )
            .Callback<RedisKey, RedisValue, RedisValue, When, CommandFlags>( ( _, _, value, _, _ ) => {
                reviewJson = value;
                reviewWriteOrder = Interlocked.Increment( ref sequence );
            } )
            .ReturnsAsync( true );
        _ = database.Setup( db => db.KeyDeleteAsync(
                "cache:refresh:pending:refresh-saga", It.IsAny<CommandFlags>( ) ) )
            .Callback( ( ) => pendingDeleteOrder = Interlocked.Increment( ref sequence ) )
            .ReturnsAsync( true );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings { JobExpirationMinutes = 60 } ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );
        await store.MarkUnresolvedAsync(
            "refresh-saga", "No provider result.", TestContext.CancellationToken );

        RefreshReviewEntry? unresolved = JsonSerializer.Deserialize<RefreshReviewEntry>(
            reviewJson.ToString( ), s_jsonOptions );
        Assert.IsNotNull( unresolved );
        Assert.IsNotNull( unresolved.FailedAt );
        Assert.AreEqual( "No provider result.", unresolved.FailureReason );
        Assert.IsLessThan( pendingDeleteOrder, reviewWriteOrder );
    }

    /// <summary>An ordinary interactive miss has no pending refresh context and is never reviewed.</summary>
    [TestMethod]
    public async Task MarkUnresolved_WithoutPendingContext_DoesNotCreateReviewEntry( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        _ = database.Setup( db => db.StringGetAsync(
                "cache:refresh:pending:interactive-saga", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( RedisValue.Null );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );
        await store.MarkUnresolvedAsync(
            "interactive-saga", "No provider result.", TestContext.CancellationToken );

        database.Verify( db => db.HashSetAsync(
            "cache:refresh:unresolved", It.IsAny<RedisValue>( ), It.IsAny<RedisValue>( ),
            It.IsAny<When>( ), It.IsAny<CommandFlags>( ) ), Times.Never );
    }

    /// <summary>The review list deserializes entries and returns newest failures first.</summary>
    [TestMethod]
    public async Task GetUnresolved_ReturnsNewestFirst( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );

        RefreshReviewEntry older = CreateEntry( ) with {
            SourceRecordUri = "at://did:plc:test/link.bridgebeats.lookup/track:older",
            FailedAt = DateTimeOffset.UtcNow.AddMinutes( -5 )
        };
        RefreshReviewEntry newer = CreateEntry( ) with { FailedAt = DateTimeOffset.UtcNow };
        _ = database.Setup( db => db.HashGetAllAsync(
                "cache:refresh:unresolved", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [
                new HashEntry( older.SourceRecordUri, JsonSerializer.Serialize( older, s_jsonOptions ) ),
                new HashEntry( newer.SourceRecordUri, JsonSerializer.Serialize( newer, s_jsonOptions ) )
            ] );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );
        IReadOnlyList<RefreshReviewEntry> entries = await store.GetUnresolvedAsync( TestContext.CancellationToken );

        Assert.HasCount( 2, entries );
        Assert.AreEqual( newer.SourceRecordUri, entries[0].SourceRecordUri );
    }

    /// <summary>A successful completion removes the unresolved entry indexed by that same saga.</summary>
    [TestMethod]
    public async Task Complete_UnresolvedSaga_RemovesReviewEntry( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        _ = database.Setup( db => db.HashGetAsync(
                "cache:refresh:unresolved:sagas", "refresh-saga", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( CreateEntry( ).SourceRecordUri );
        _ = database.Setup( db => db.HashGetAsync(
                "cache:refresh:unresolved", CreateEntry( ).SourceRecordUri, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( JsonSerializer.Serialize( CreateEntry( ), s_jsonOptions ) );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );

        await store.CompleteAsync( "refresh-saga", TestContext.CancellationToken );

        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved", CreateEntry( ).SourceRecordUri, It.IsAny<CommandFlags>( ) ), Times.Once );
        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved:sagas", "refresh-saga", It.IsAny<CommandFlags>( ) ), Times.Once );
    }

    /// <summary>A successful replacement refresh clears an older review for the same source URI.</summary>
    [TestMethod]
    public async Task Complete_LaterPendingSaga_RemovesOlderReviewEntry( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        RefreshReviewEntry older = CreateEntry( ) with { SagaId = "older-saga" };
        RefreshReviewEntry replacement = CreateEntry( ) with { SagaId = "replacement-saga" };
        _ = database.Setup( db => db.StringGetAsync(
                "cache:refresh:pending:replacement-saga", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( JsonSerializer.Serialize( replacement, s_jsonOptions ) );
        _ = database.Setup( db => db.HashGetAsync(
                "cache:refresh:unresolved", older.SourceRecordUri, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( JsonSerializer.Serialize( older, s_jsonOptions ) );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );

        await store.CompleteAsync( "replacement-saga", TestContext.CancellationToken );

        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved", older.SourceRecordUri, It.IsAny<CommandFlags>( ) ), Times.Once );
        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved:sagas", "older-saga", It.IsAny<CommandFlags>( ) ), Times.Once );
    }

    /// <summary>A stale saga index cannot remove a newer unresolved review for the same source URI.</summary>
    [TestMethod]
    public async Task Complete_StaleSagaIndex_PreservesNewerReviewEntry( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        RefreshReviewEntry newer = CreateEntry( ) with { SagaId = "newer-saga" };
        _ = database.Setup( db => db.HashGetAsync(
                "cache:refresh:unresolved:sagas", "older-saga", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( newer.SourceRecordUri );
        _ = database.Setup( db => db.HashGetAsync(
                "cache:refresh:unresolved", newer.SourceRecordUri, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( JsonSerializer.Serialize( newer, s_jsonOptions ) );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );

        await store.CompleteAsync( "older-saga", TestContext.CancellationToken );

        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved", newer.SourceRecordUri, It.IsAny<CommandFlags>( ) ), Times.Never );
        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved:sagas", "older-saga", It.IsAny<CommandFlags>( ) ), Times.Once );
    }

    /// <summary>A corrupt hash value is quarantined without hiding healthy review entries.</summary>
    [TestMethod]
    public async Task GetUnresolved_CorruptEntry_QuarantinesAndContinues( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        RefreshReviewEntry healthy = CreateEntry( ) with { FailedAt = DateTimeOffset.UtcNow };
        _ = database.Setup( db => db.HashGetAllAsync(
                "cache:refresh:unresolved", It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [
                new HashEntry( "corrupt-uri", "{not-json" ),
                new HashEntry( healthy.SourceRecordUri, JsonSerializer.Serialize( healthy, s_jsonOptions ) )
            ] );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );
        IReadOnlyList<RefreshReviewEntry> entries = await store.GetUnresolvedAsync( TestContext.CancellationToken );

        Assert.HasCount( 1, entries );
        Assert.AreEqual( healthy.SourceRecordUri, entries[0].SourceRecordUri );
        database.Verify( db => db.HashSetAsync(
            "cache:refresh:unresolved:corrupt", "corrupt-uri", "{not-json",
            It.IsAny<When>( ), It.IsAny<CommandFlags>( ) ), Times.Once );
        database.Verify( db => db.HashDeleteAsync(
            "cache:refresh:unresolved", "corrupt-uri", It.IsAny<CommandFlags>( ) ), Times.Once );
    }

    /// <summary>Explicit cleanup passes the displayed saga and CID through the atomic Redis CAS.</summary>
    [TestMethod]
    public async Task DeleteUnresolved_MatchingRevision_ReturnsTrue( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        _ = database.Setup( db => db.ScriptEvaluateAsync(
                It.IsAny<string>( ),
                It.Is<RedisKey[]?>( keys => keys != null && keys.Length == 2 ),
                It.Is<RedisValue[]?>( values => values != null
                    && values.Length == 3
                    && values[0] == CreateEntry( ).SourceRecordUri
                    && values[1] == CreateEntry( ).SagaId
                    && values[2] == CreateEntry( ).SourceRecordCid ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisResult)RedisResult.Create( (RedisValue)1 ) );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );

        bool deleted = await store.DeleteUnresolvedAsync(
            CreateEntry( ).SourceRecordUri,
            CreateEntry( ).SagaId,
            CreateEntry( ).SourceRecordCid!,
            TestContext.CancellationToken );

        Assert.IsTrue( deleted );
    }

    /// <summary>A Redis CAS mismatch preserves the replacement review entry.</summary>
    [TestMethod]
    public async Task DeleteUnresolved_RevisionMismatch_ReturnsFalse( ) {
        Mock<IConnectionMultiplexer> redis = new( );
        Mock<IDatabase> database = new( );
        _ = redis.Setup( connection => connection.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( database.Object );
        _ = database.Setup( db => db.ScriptEvaluateAsync(
                It.IsAny<string>( ), It.IsAny<RedisKey[]?>( ), It.IsAny<RedisValue[]?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisResult)RedisResult.Create( (RedisValue)0 ) );

        RedisRefreshReviewStore store = new(
            redis.Object,
            Options.Create( new QueueSettings( ) ),
            Mock.Of<ILogger<RedisRefreshReviewStore>>( ) );

        bool deleted = await store.DeleteUnresolvedAsync(
            CreateEntry( ).SourceRecordUri,
            "stale-saga",
            "bafyreistale",
            TestContext.CancellationToken );

        Assert.IsFalse( deleted );
    }

    private static RefreshReviewEntry CreateEntry( ) => new( ) {
        SourceRecordUri = "at://did:plc:test/link.bridgebeats.lookup/track:USRC12345678",
        SourceRecordCid = "bafyreihash",
        SagaId = "refresh-saga",
        LookupType = LookupRequestType.IsrcLookup,
        LookupValue = "USRC12345678"
    };
}
