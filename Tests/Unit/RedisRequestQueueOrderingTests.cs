using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RedisRequestQueue{T}"/>'s priority-stream dequeue ordering
/// (<c>GetStreamDequeueOrder</c>). Verify the normal Interactive-first ordering, the anti-starvation
/// "aging slot" every Nth dequeue that promotes a lower lane (alternating between background-first and
/// bulk-first across slots), determinism for identical inputs, the misconfiguration fallback when
/// <c>InteractiveAgingInterval</c> is &lt;= 1 (logs a warning and uses the default of 8), and bulk-lane
/// gating by the configured minimum bulk threshold.
/// </summary>
[TestClass]
public class RedisRequestQueueOrderingTests {

    /// <summary>
    /// Builds a Spotify-bound <see cref="RedisRequestQueue{T}"/> with the given aging interval and
    /// minimum bulk threshold over a bare Redis mock (only the ordering logic is exercised).
    /// </summary>
    private static RedisRequestQueue<QueuedLookupRequest> CreateQueue( int agingInterval = 8, int minBulkThreshold = 0 ) {
        Mock<IConnectionMultiplexer> redisMock = new( );
        // GetDatabase() is called during GetStreamDequeueOrder only indirectly via GetDepthAsync;
        // since we call GetStreamDequeueOrder directly (bypassing async), no IDatabase is needed.
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> loggerMock = new( );
        IOptions<QueueSettings> settings = Options.Create( new QueueSettings {
            InteractiveAgingInterval = agingInterval,
            DefaultMinBulkQueueThreshold = minBulkThreshold
        } );
        return new RedisRequestQueue<QueuedLookupRequest>(
            redisMock.Object,
            loggerMock.Object,
            settings,
            SupportedProviders.Spotify
        );
    }

    /// <summary>Builds a depth snapshot whose bulk lane is non-empty (default 99 items).</summary>
    private static QueueDepth DepthWithBulk( int bulk = 99 ) => new( Interactive: 5, Background: 5, Bulk: bulk, Total: 5 + 5 + bulk );

    /// <summary>Builds a depth snapshot with an empty bulk lane.</summary>
    private static QueueDepth DepthWithoutBulk( ) => new( Interactive: 5, Background: 5, Bulk: 0, Total: 10 );

    /// <summary>
    /// On a non-aging dequeue (counter 1), the order leads with the interactive stream.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_NonAgingCall_InteractiveFirst( ) {
        // Arrange
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: 8 );
        QueueDepth depth = DepthWithBulk( );

        // Act — counter=1 (not a multiple of 8)
        string[] order = queue.GetStreamDequeueOrder( 1, depth );

        // Assert — interactive stream is first
        Assert.IsTrue( order[0].EndsWith( ":interactive", StringComparison.Ordinal ),
            $"Expected interactive first at counter=1, got: {order[0]}" );
    }

    /// <summary>
    /// On an aging-slot dequeue (counter == N), the order leads with a lower tier rather than
    /// interactive.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_AtNthCall_LowerTierLeads( ) {
        // Arrange
        const int N = 8;
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: N );
        QueueDepth depth = DepthWithBulk( );

        // Act
        string[] order = queue.GetStreamDequeueOrder( N, depth );

        // Assert — first stream is NOT interactive
        Assert.IsFalse( order[0].EndsWith( ":interactive", StringComparison.Ordinal ),
            $"Expected non-interactive first at counter=N={N}, got: {order[0]}" );
    }

    /// <summary>
    /// Across a 2N window of dequeues, both the background and bulk streams lead at least once,
    /// confirming aging slots promote both lower lanes.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_TwoNWindow_BackgroundAndBulkEachLead( ) {
        // Arrange
        const int N = 8;
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: N );
        QueueDepth depth = DepthWithBulk( 99 ); // bulk well above threshold

        bool backgroundLed = false;
        bool bulkLed = false;

        // Act — check all counters in [1..2N]
        for (int counter = 1; counter <= 2 * N; counter++) {
            string[] order = queue.GetStreamDequeueOrder( counter, depth );
            if (order[0].EndsWith( ":background", StringComparison.Ordinal )) { backgroundLed = true; }
            if (order[0].EndsWith( ":bulk", StringComparison.Ordinal )) { bulkLed = true; }
        }

        // Assert
        Assert.IsTrue( backgroundLed, "Background stream must lead at least once in a 2N window" );
        Assert.IsTrue( bulkLed, "Bulk stream must lead at least once in a 2N window" );
    }

    /// <summary>
    /// Consecutive aging slots (at N and 2N) alternate the promoted lower tier, and neither aging
    /// slot leads with interactive.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_RotationAlternates_BackgroundAndBulk( ) {
        // Arrange
        const int N = 8;
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: N );
        QueueDepth depth = DepthWithBulk( 99 );

        // Act
        string firstAgingSlotLeader = queue.GetStreamDequeueOrder( N, depth )[0];
        string secondAgingSlotLeader = queue.GetStreamDequeueOrder( 2 * N, depth )[0];

        // Assert — the two aging slots must promote different tiers
        Assert.AreNotEqual( firstAgingSlotLeader, secondAgingSlotLeader,
            "Consecutive aging slots must alternate the promoted tier" );

        // Both must be non-interactive
        Assert.IsFalse( firstAgingSlotLeader.EndsWith( ":interactive", StringComparison.Ordinal ),
            $"First aging slot must not lead with interactive, got: {firstAgingSlotLeader}" );
        Assert.IsFalse( secondAgingSlotLeader.EndsWith( ":interactive", StringComparison.Ordinal ),
            $"Second aging slot must not lead with interactive, got: {secondAgingSlotLeader}" );
    }

    /// <summary>
    /// Every non-aging dequeue (counters not divisible by N) leads with the interactive stream.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_NonAgingCalls_AlwaysInteractiveFirst( ) {
        // Arrange
        const int N = 8;
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: N );
        QueueDepth depth = DepthWithBulk( );

        // Act + Assert — every counter in [1..3N] that is NOT a multiple of N
        for (int counter = 1; counter <= 3 * N; counter++) {
            if (counter % N == 0) { continue; } // skip aging slots
            string[] order = queue.GetStreamDequeueOrder( counter, depth );
            Assert.IsTrue( order[0].EndsWith( ":interactive", StringComparison.Ordinal ),
                $"Expected interactive first at counter={counter} (non-aging), got: {order[0]}" );
        }
    }

    /// <summary>
    /// The ordering is deterministic: repeated calls with the same counter and depth produce the same
    /// stream order.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_SameInputs_ProduceSameOutput( ) {
        // Arrange
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( );
        QueueDepth depth = DepthWithBulk( );

        // Act — call 10 times with the same counter
        string[][] results = [..Enumerable.Range( 0, 10 )
            .Select( _ => queue.GetStreamDequeueOrder( 5, depth ) )];

        // Assert — all calls return the same order
        for (int i = 1; i < results.Length; i++) {
            CollectionAssert.AreEqual(
                results[0],
                results[i],
                $"Call {i} produced a different order than call 0 — ordering must be deterministic" );
        }
    }

    /// <summary>
    /// An <c>InteractiveAgingInterval</c> of 1 is treated as a misconfiguration: the queue falls back
    /// to the default of 8 (so counter 1 is not an aging slot and leads with interactive) and logs a
    /// warning.
    /// </summary>
    [TestMethod]
    public void Constructor_AgingIntervalOne_FallsBackToDefault( ) {
        // Arrange — N=1 (misconfiguration)
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> loggerMock = new( );
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Without this setup Moq returns false and Log() is never invoked.
        _ = loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        IOptions<QueueSettings> settings = Options.Create( new QueueSettings {
            InteractiveAgingInterval = 1,
            DefaultMinBulkQueueThreshold = 0
        } );
        RedisRequestQueue<QueuedLookupRequest> queue = new(
            new Mock<IConnectionMultiplexer>( ).Object,
            loggerMock.Object,
            settings,
            SupportedProviders.Spotify
        );
        QueueDepth depth = DepthWithBulk( );

        // Act — counter=1: if N fell back to 8, this is NOT an aging slot → interactive first
        string[] order = queue.GetStreamDequeueOrder( 1, depth );

        // Assert — interactive must be first (default=8 applied, not N=1)
        Assert.IsTrue( order[0].EndsWith( ":interactive", StringComparison.Ordinal ),
            $"With N=1 falling back to default=8, counter=1 is not an aging slot; expected interactive first, got: {order[0]}" );

        // Assert — a Warning log was emitted about the misconfiguration
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce( ),
            "Expected a Warning log when InteractiveAgingInterval ≤ 1" );
    }

    /// <summary>
    /// An <c>InteractiveAgingInterval</c> of 0 is treated as a misconfiguration: the queue falls back
    /// to the default of 8 and logs a warning.
    /// </summary>
    [TestMethod]
    public void Constructor_AgingIntervalZero_FallsBackToDefault( ) {
        // Arrange
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> loggerMock = new( );
        // [LoggerMessage] source-generated code gates every call with IsEnabled().
        // Without this setup Moq returns false and Log() is never invoked.
        _ = loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        IOptions<QueueSettings> settings = Options.Create( new QueueSettings {
            InteractiveAgingInterval = 0,
            DefaultMinBulkQueueThreshold = 0
        } );
        RedisRequestQueue<QueuedLookupRequest> queue = new(
            new Mock<IConnectionMultiplexer>( ).Object,
            loggerMock.Object,
            settings,
            SupportedProviders.Spotify
        );
        QueueDepth depth = DepthWithBulk( );

        // Act
        string[] order = queue.GetStreamDequeueOrder( 1, depth );

        // Assert — interactive must be first (defaulted to 8)
        Assert.IsTrue( order[0].EndsWith( ":interactive", StringComparison.Ordinal ),
            $"With N=0 falling back to default=8, counter=1 is not an aging slot; expected interactive first, got: {order[0]}" );

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce( ),
            "Expected a Warning log when InteractiveAgingInterval ≤ 1" );
    }

    /// <summary>
    /// When the bulk depth is below the minimum bulk threshold, the bulk stream is excluded from the
    /// order, leaving only interactive and background.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_BulkBelowThreshold_BulkExcluded( ) {
        // Arrange — threshold=5, bulk=0
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: 8, minBulkThreshold: 5 );
        QueueDepth depth = DepthWithoutBulk( ); // bulk=0

        // Act
        string[] order = queue.GetStreamDequeueOrder( 1, depth );

        // Assert — bulk must not appear
        string? bulkEntry = order.FirstOrDefault( s => s.EndsWith( ":bulk", StringComparison.Ordinal ) );
        Assert.IsNull( bulkEntry, "Bulk stream must be excluded when depth is below threshold" );
        Assert.HasCount( 2, order, "Only interactive and background streams expected" );
    }

    /// <summary>
    /// When the bulk depth meets the minimum bulk threshold, the bulk stream is included, yielding all
    /// three streams in the order.
    /// </summary>
    [TestMethod]
    public void GetStreamDequeueOrder_BulkAtThreshold_BulkIncluded( ) {
        // Arrange — threshold=5, bulk=5
        RedisRequestQueue<QueuedLookupRequest> queue = CreateQueue( agingInterval: 8, minBulkThreshold: 5 );
        QueueDepth depth = new( Interactive: 1, Background: 1, Bulk: 5, Total: 7 );

        // Act — non-aging call
        string[] order = queue.GetStreamDequeueOrder( 1, depth );

        // Assert — bulk must appear
        string? bulkEntry = order.FirstOrDefault( s => s.EndsWith( ":bulk", StringComparison.Ordinal ) );
        Assert.IsNotNull( bulkEntry, "Bulk stream must be included when depth meets threshold" );
        Assert.HasCount( 3, order, "All three streams expected" );
    }
}
