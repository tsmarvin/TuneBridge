using System.Text.RegularExpressions;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="RedisRequestQueue{T}"/> using the shared Redis container.
/// Requires Docker to be running on the host machine.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
public partial class RedisRequestQueueTests {

    private static IConnectionMultiplexer? s_redis;

    private Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> _mockLogger = null!;
    private IOptions<QueueSettings> _settings = null!;
    private RedisRequestQueue<QueuedLookupRequest> _queue = null!;

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
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Cleans up the Redis connection after all tests in this class have completed.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears queue-related keys and creates a fresh queue instance before each test.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        // Clear only queue-related keys before each test
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "queue:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }

        _mockLogger = new Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>>( );
        _settings = Options.Create( new QueueSettings( ) );

        _queue = new RedisRequestQueue<QueuedLookupRequest>(
            s_redis,
            _mockLogger.Object,
            _settings,
            SupportedProviders.Spotify
        );

        // Ensure consumer groups exist
        await _queue.EnsureConsumerGroupsAsync( CancellationToken.None );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.EnqueueAsync"/> adds messages to the interactive priority stream.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueAsync_AddsMessageToInteractiveStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );

        // Act
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 1, depth.Interactive );
        Assert.AreEqual( 0, depth.Background );
        Assert.AreEqual( 0, depth.Bulk );
        Assert.AreEqual( 1, depth.Total );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.EnqueueAsync"/> adds messages to the background priority stream.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueAsync_AddsMessageToBackgroundStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );

        // Act
        await _queue.EnqueueAsync( request, QueuePriority.Background, TestContext.CancellationToken );

        // Assert
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 0, depth.Interactive );
        Assert.AreEqual( 1, depth.Background );
        Assert.AreEqual( 0, depth.Bulk );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.EnqueueAsync"/> adds messages to the bulk priority stream.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task EnqueueAsync_AddsMessageToBulkStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );

        // Act
        await _queue.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 0, depth.Interactive );
        Assert.AreEqual( 0, depth.Background );
        Assert.AreEqual( 1, depth.Bulk );
    }

    /// <summary>
    /// Verifies that DequeueAsync returns a message when one is available.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DequeueAsync_ReturnsMessage_WhenAvailable( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Act
        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( message );
        Assert.AreEqual( request.RequestId, message.Payload.RequestId );
        Assert.AreEqual( request.SagaId, message.Payload.SagaId );
        Assert.AreEqual( request.LookupValue, message.Payload.LookupValue );
    }

    /// <summary>
    /// Verifies that DequeueAsync returns null when the queue is empty.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DequeueAsync_ReturnsNull_WhenQueueEmpty( ) {
        // Act
        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );

        // Assert
        Assert.IsNull( message );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.AcknowledgeAsync"/> removes the message from the stream.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task AcknowledgeAsync_RemovesMessageFromStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( message );

        // Act
        await _queue.AcknowledgeAsync( message.MessageId, TestContext.CancellationToken );

        // Assert - queue should be empty after ack
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 0, depth.Total );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.MoveToDlqAsync"/> moves the message to the dead letter queue.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task MoveToDlqAsync_MovesMessageToDlq( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( message );

        // Act
        await _queue.MoveToDlqAsync( message.MessageId, "Test failure reason", TestContext.CancellationToken );

        // Assert - main queue empty, DLQ has message
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 0, depth.Total );

        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> dlqMessages = await _queue.GetDlqMessagesAsync( 10, TestContext.CancellationToken );
        Assert.HasCount( 1, dlqMessages );
        Assert.AreEqual( request.RequestId, dlqMessages[0].Payload.RequestId );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.RequeueFromDlqAsync"/> moves the message back from DLQ to the main queue.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RequeueFromDlqAsync_MovesMessageBackToQueue( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( message );

        await _queue.MoveToDlqAsync( message.MessageId, "Test failure", TestContext.CancellationToken );

        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> dlqMessages = await _queue.GetDlqMessagesAsync( 10, TestContext.CancellationToken );
        Assert.HasCount( 1, dlqMessages );

        // Act
        await _queue.RequeueFromDlqAsync( dlqMessages[0].MessageId, QueuePriority.Background, TestContext.CancellationToken );

        // Assert - DLQ empty, background queue has message
        dlqMessages = await _queue.GetDlqMessagesAsync( 10, TestContext.CancellationToken );
        Assert.IsEmpty( dlqMessages );

        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 1, depth.Background );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.DeleteFromDlqAsync"/> permanently removes the message from the DLQ.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DeleteFromDlqAsync_RemovesMessageFromDlq( ) {
        // Arrange - use Interactive priority to avoid bulk gating
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( message );

        await _queue.MoveToDlqAsync( message.MessageId, "Test failure", TestContext.CancellationToken );

        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> dlqMessages = await _queue.GetDlqMessagesAsync( 10, TestContext.CancellationToken );
        Assert.HasCount( 1, dlqMessages );

        // Act
        bool deleted = await _queue.DeleteFromDlqAsync( dlqMessages[0].MessageId, TestContext.CancellationToken );

        // Assert
        Assert.IsTrue( deleted );

        dlqMessages = await _queue.GetDlqMessagesAsync( 10, TestContext.CancellationToken );
        Assert.IsEmpty( dlqMessages );
    }

    /// <summary>
    /// Verifies that queues for different providers are isolated and do not share messages.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task MultipleProviderQueues_AreIsolated( ) {
        // Arrange
        RedisRequestQueue<QueuedLookupRequest> spotifyQueue = _queue;

        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> appleLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> appleQueue = new(
            s_redis!,
            appleLogger.Object,
            _settings,
            SupportedProviders.AppleMusic
        );
        await appleQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        QueuedLookupRequest spotifyRequest = CreateTestRequest( provider: SupportedProviders.Spotify );
        QueuedLookupRequest appleRequest = CreateTestRequest( provider: SupportedProviders.AppleMusic );

        // Act
        await spotifyQueue.EnqueueAsync( spotifyRequest, QueuePriority.Interactive, TestContext.CancellationToken );
        await appleQueue.EnqueueAsync( appleRequest, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert - each queue has its own message
        QueueDepth spotifyDepth = await spotifyQueue.GetDepthAsync( TestContext.CancellationToken );
        QueueDepth appleDepth = await appleQueue.GetDepthAsync( TestContext.CancellationToken );

        Assert.AreEqual( 1, spotifyDepth.Interactive );
        Assert.AreEqual( 1, appleDepth.Interactive );

        // Dequeue from Spotify should get Spotify request
        QueuedMessage<QueuedLookupRequest>? spotifyMessage = await spotifyQueue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( spotifyMessage );
        Assert.AreEqual( SupportedProviders.Spotify, spotifyMessage.Payload.Provider );

        // Dequeue from Apple should get Apple request
        QueuedMessage<QueuedLookupRequest>? appleMessage = await appleQueue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( appleMessage );
        Assert.AreEqual( SupportedProviders.AppleMusic, appleMessage.Payload.Provider );
    }

    /// <summary>
    /// Verifies that weighted priority selection processes all messages across all priority levels.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task WeightedPrioritySelection_ProcessesAllMessages( ) {
        // Arrange - add multiple messages to each priority
        const int MessagesPerPriority = 10;

        // Create a queue with bulk gating disabled for this test
        IOptions<QueueSettings> testSettings = Options.Create( new QueueSettings {
            DefaultMinBulkQueueThreshold = 0 // Disable bulk gating
        } );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> testLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> testQueue = new(
            s_redis!,
            testLogger.Object,
            testSettings,
            SupportedProviders.Spotify
        );
        await testQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        for (int i = 0; i < MessagesPerPriority; i++) {
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Interactive, TestContext.CancellationToken );
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Background, TestContext.CancellationToken );
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Bulk, TestContext.CancellationToken );
        }

        // Act - dequeue all and track which priorities are drained first
        // With weighted selection, all messages should eventually be processed
        int totalDequeued = 0;

        QueuedMessage<QueuedLookupRequest>? message;
        while ((message = await testQueue.DequeueAsync( TestContext.CancellationToken )) is not null) {
            await testQueue.AcknowledgeAsync( message.MessageId, TestContext.CancellationToken );
            totalDequeued++;
        }

        // Assert - all messages should be processed
        Assert.AreEqual( MessagesPerPriority * 3, totalDequeued, "All messages should be dequeued" );

        QueueDepth depth = await testQueue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 0, depth.Total, "Queue should be empty after processing all messages" );
    }

    /// <summary>
    /// Verifies that Redis consumer groups allow multiple worker instances to process messages without duplication.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ConsumerGroups_AllowMultipleWorkerInstances( ) {
        // Arrange - create two queue instances (simulating two workers)
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> logger2 = new( );
        RedisRequestQueue<QueuedLookupRequest> worker2Queue = new(
            s_redis!,
            logger2.Object,
            _settings,
            SupportedProviders.Spotify
        );
        await worker2Queue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        // Enqueue multiple messages
        const int MessageCount = 10;
        for (int i = 0; i < MessageCount; i++) {
            await _queue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Interactive, TestContext.CancellationToken );
        }

        // Act - both workers dequeue concurrently
        int worker1Count = 0;
        int worker2Count = 0;

        for (int i = 0; i < MessageCount; i++) {
            // Alternate between workers
            if (i % 2 == 0) {
                QueuedMessage<QueuedLookupRequest>? msg = await _queue.DequeueAsync( TestContext.CancellationToken );
                if (msg is not null) {
                    worker1Count++;
                    await _queue.AcknowledgeAsync( msg.MessageId, TestContext.CancellationToken );
                }
            } else {
                QueuedMessage<QueuedLookupRequest>? msg = await worker2Queue.DequeueAsync( TestContext.CancellationToken );
                if (msg is not null) {
                    worker2Count++;
                    await worker2Queue.AcknowledgeAsync( msg.MessageId, TestContext.CancellationToken );
                }
            }
        }

        // Assert - both workers processed messages, no duplicates
        Assert.AreEqual( MessageCount, worker1Count + worker2Count );
        Assert.IsGreaterThan( 0, worker1Count, "Worker 1 should have processed at least one message" );
        Assert.IsGreaterThan( 0, worker2Count, "Worker 2 should have processed at least one message" );

        // Queue should be empty
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 0, depth.Total );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.RequeueAsync"/> puts the message back in the queue for retry.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RequeueAsync_PutsMessageBackForRetry( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( message );

        // Simulate temporary failure - requeue with delay
        await _queue.RequeueAsync( message.MessageId, TimeSpan.FromMilliseconds( 100 ), TestContext.CancellationToken );

        // Assert - message should be back in queue
        QueueDepth depth = await _queue.GetDepthAsync( TestContext.CancellationToken );
        Assert.AreEqual( 1, depth.Total );

        // Should be able to dequeue again
        QueuedMessage<QueuedLookupRequest>? requeued = await _queue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( requeued );
        Assert.AreEqual( request.RequestId, requeued.Payload.RequestId );
    }

    /// <summary>
    /// Verifies that consumer IDs are unique across multiple queue instances and follow expected format.
    /// </summary>
    [TestMethod]
    public void ConsumerIds_AreUniqueAndWellFormatted( ) {
        // Arrange - create multiple queue instances for the same provider
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> logger1 = new( );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> logger2 = new( );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> logger3 = new( );

        RedisRequestQueue<QueuedLookupRequest> queue1 = new(
            s_redis!,
            logger1.Object,
            _settings,
            SupportedProviders.Spotify
        );

        RedisRequestQueue<QueuedLookupRequest> queue2 = new(
            s_redis!,
            logger2.Object,
            _settings,
            SupportedProviders.Spotify
        );

        RedisRequestQueue<QueuedLookupRequest> queue3 = new(
            s_redis!,
            logger3.Object,
            _settings,
            SupportedProviders.AppleMusic
        );

        // Act - get consumer IDs using reflection
        System.Reflection.FieldInfo? consumerIdField = typeof( RedisRequestQueue<QueuedLookupRequest> )
            .GetField( "_consumerId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );
        Assert.IsNotNull( consumerIdField, "Could not find _consumerId field" );

        string? consumerId1 = consumerIdField.GetValue( queue1 ) as string;
        string? consumerId2 = consumerIdField.GetValue( queue2 ) as string;
        string? consumerId3 = consumerIdField.GetValue( queue3 ) as string;

        // Assert - consumer IDs should be unique
        Assert.IsNotNull( consumerId1 );
        Assert.IsNotNull( consumerId2 );
        Assert.IsNotNull( consumerId3 );

        Assert.AreNotEqual( consumerId1, consumerId2, "Consumer IDs for same provider should be unique" );
        Assert.AreNotEqual( consumerId1, consumerId3, "Consumer IDs for different providers should be unique" );
        Assert.AreNotEqual( consumerId2, consumerId3, "Consumer IDs for different providers should be unique" );

        // Assert - format should be "{provider}-worker-{guid}"
        // Spotify consumer IDs should start with "spotify-worker-"
        Assert.IsTrue( consumerId1.StartsWith( "spotify-worker-", StringComparison.OrdinalIgnoreCase ),
            $"Consumer ID '{consumerId1}' should start with 'spotify-worker-'" );
        Assert.IsTrue( consumerId2.StartsWith( "spotify-worker-", StringComparison.OrdinalIgnoreCase ),
            $"Consumer ID '{consumerId2}' should start with 'spotify-worker-'" );

        // Apple Music consumer IDs should start with "applemusic-worker-"
        Assert.IsTrue( consumerId3.StartsWith( "applemusic-worker-", StringComparison.OrdinalIgnoreCase ),
            $"Consumer ID '{consumerId3}' should start with 'applemusic-worker-'" );

        // Assert - GUID portion should be 32 characters (format N) and be valid hex strings
        const string SpotifyPrefix = "spotify-worker-";
        const string AppleMusicPrefix = "applemusic-worker-";

        string guidPart1 = consumerId1[ SpotifyPrefix.Length..];
        string guidPart2 = consumerId2[ SpotifyPrefix.Length..];
        string guidPart3 = consumerId3[ AppleMusicPrefix.Length..];

        Assert.AreEqual( 32, guidPart1.Length, "GUID portion should be 32 characters" );
        Assert.AreEqual( 32, guidPart2.Length, "GUID portion should be 32 characters" );
        Assert.AreEqual( 32, guidPart3.Length, "GUID portion should be 32 characters" );

        Assert.IsTrue( GuidHexRegex( ).IsMatch( guidPart1 ),
            $"GUID portion '{guidPart1}' should be a valid hex string" );
        Assert.IsTrue( GuidHexRegex( ).IsMatch( guidPart2 ),
            $"GUID portion '{guidPart2}' should be a valid hex string" );
        Assert.IsTrue( GuidHexRegex( ).IsMatch( guidPart3 ),
            $"GUID portion '{guidPart3}' should be a valid hex string" );

        // Assert - total length should be reasonable (provider name + "-worker-" + 32 char GUID)
        // Longest provider name is "applemusic" (10) + "-worker-" (8) + GUID (32) = 50 chars max
        Assert.IsLessThanOrEqualTo( 60, consumerId1.Length, $"Consumer ID '{consumerId1}' should be reasonably short (length: {consumerId1.Length})" );
        Assert.IsLessThanOrEqualTo( 60, consumerId2.Length, $"Consumer ID '{consumerId2}' should be reasonably short (length: {consumerId2.Length})" );
        Assert.IsLessThanOrEqualTo( 60, consumerId3.Length, $"Consumer ID '{consumerId3}' should be reasonably short (length: {consumerId3.Length})" );
    }

    // -------------------------------------------------------------------------
    // B7–B12: Interactive-first ordering with aging (new deterministic algorithm)
    // -------------------------------------------------------------------------

    /// <summary>
    /// B7 — Interactive preemption end-to-end.
    /// Background is enqueued first; then an interactive message arrives.
    /// On the very next non-aging dequeue the interactive message must be served first.
    /// Failure-first: before the deterministic algorithm, weighted-random might deliver background first.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task B7_InteractivePreemption_DequeuesInteractiveBeforeBackground( ) {
        // Arrange — background arrives first, interactive arrives second
        IOptions<QueueSettings> testSettings = Options.Create( new QueueSettings {
            DefaultMinBulkQueueThreshold = 0,
            InteractiveAgingInterval = 8 // default; first aging slot at counter=8
        } );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> testLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> testQueue = new(
            s_redis!,
            testLogger.Object,
            testSettings,
            SupportedProviders.Spotify
        );
        await testQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        QueuedLookupRequest bgRequest = CreateTestRequest( );
        QueuedLookupRequest interactiveRequest = CreateTestRequest( );

        await testQueue.EnqueueAsync( bgRequest, QueuePriority.Background, TestContext.CancellationToken );
        await testQueue.EnqueueAsync( interactiveRequest, QueuePriority.Interactive, TestContext.CancellationToken );

        // Act — counter=1 (not an aging slot); interactive must lead
        QueuedMessage<QueuedLookupRequest>? first = await testQueue.DequeueAsync( TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( first );
        Assert.AreEqual( interactiveRequest.RequestId, first.Payload.RequestId,
            "Interactive message must be dequeued before background on a normal (non-aging) slot" );
    }

    /// <summary>
    /// B8 — Bounded starvation over a >N window.
    /// Enqueues only background messages and drives N dequeues.
    /// The Nth dequeue is the first aging slot (backgroundLeads=true), so background must be served.
    /// This is the highest-value guard: verifies starvation is bounded within N calls.
    /// Failure-first: without the aging algorithm, background would never be served
    /// (interactive stream checked first every time, returning nothing, but background also checked).
    /// Actually without aging, background IS served when interactive is empty — so the real guard is
    /// that interactive does NOT block background when interactive is empty, AND that with only
    /// background+interactive mixed, aging ensures background gets a turn within N.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task B8_BoundedStarvation_BackgroundServedWithinAgingWindow( ) {
        // Arrange — use N=4 so the test stays fast; first aging slot at counter=4
        const int AgingInterval = 4;
        IOptions<QueueSettings> testSettings = Options.Create( new QueueSettings {
            DefaultMinBulkQueueThreshold = 0,
            InteractiveAgingInterval = AgingInterval
        } );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> testLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> testQueue = new(
            s_redis!,
            testLogger.Object,
            testSettings,
            SupportedProviders.Spotify
        );
        await testQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        // Enqueue AgingInterval interactive messages AND AgingInterval background messages
        // so interactive stream is not empty on the first (N-1) dequeues.
        const int MsgCount = AgingInterval;
        for (int i = 0; i < MsgCount; i++) {
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Interactive, TestContext.CancellationToken );
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Background, TestContext.CancellationToken );
        }

        // Act — dequeue exactly N times (counters 1..N), tracking priority of each result
        List<QueuePriority> servedPriorities = [];
        for (int call = 1; call <= AgingInterval; call++) {
            QueuedMessage<QueuedLookupRequest>? msg = await testQueue.DequeueAsync( TestContext.CancellationToken );
            Assert.IsNotNull( msg, $"Expected a message on dequeue call {call}" );

            // Determine which stream the message came from (messageId starts with stream key)
            QueuePriority priority = msg.MessageId.Contains( ":background:" ) || msg.MessageId.Contains( "background:" )
                ? QueuePriority.Background
                : QueuePriority.Interactive;
            servedPriorities.Add( priority );

            await testQueue.AcknowledgeAsync( msg.MessageId, TestContext.CancellationToken );
        }

        // Assert — at least one background message must have been served (the aging slot at call N)
        bool backgroundServed = servedPriorities.Contains( QueuePriority.Background );
        Assert.IsTrue( backgroundServed,
            $"Background must be served within {AgingInterval} dequeues (aging slot at call {AgingInterval}). " +
            $"Served: [{string.Join( ", ", servedPriorities )}]" );

        // Assert — calls 1..(N-1) must all be interactive (aging slot only at call N)
        for (int i = 0; i < AgingInterval - 1; i++) {
            Assert.AreEqual( QueuePriority.Interactive, servedPriorities[i],
                $"Call {i + 1} (non-aging slot) must serve interactive; served {servedPriorities[i]}" );
        }

        // Assert — call N must be background (first aging slot, backgroundLeads=true for agingSlotIndex=1)
        Assert.AreEqual( QueuePriority.Background, servedPriorities[AgingInterval - 1],
            $"Call {AgingInterval} (aging slot) must serve background; served {servedPriorities[AgingInterval - 1]}" );
    }

    /// <summary>
    /// B9 — Bulk gating two-phase.
    /// Phase 1: bulk depth below threshold → bulk messages not served.
    /// Phase 2: bulk depth reaches threshold → bulk messages eligible.
    /// Failure-first: without bulk gating, bulk would be served in phase 1.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task B9_BulkGating_TwoPhase_BulkServedOnlyAboveThreshold( ) {
        const int BulkThreshold = 3;
        IOptions<QueueSettings> testSettings = Options.Create( new QueueSettings {
            DefaultMinBulkQueueThreshold = BulkThreshold,
            InteractiveAgingInterval = 100 // large N so aging slot doesn't interfere
        } );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> testLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> testQueue = new(
            s_redis!,
            testLogger.Object,
            testSettings,
            SupportedProviders.Spotify
        );
        await testQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        // Phase 1: enqueue fewer bulk messages than threshold (BulkThreshold - 1)
        QueuedLookupRequest bgRequest = CreateTestRequest( );
        await testQueue.EnqueueAsync( bgRequest, QueuePriority.Background, TestContext.CancellationToken );
        for (int i = 0; i < BulkThreshold - 1; i++) {
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Bulk, TestContext.CancellationToken );
        }

        // Dequeue — must get background (bulk gated out)
        QueuedMessage<QueuedLookupRequest>? phase1Msg = await testQueue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( phase1Msg );
        Assert.AreEqual( bgRequest.RequestId, phase1Msg.Payload.RequestId,
            "Phase 1: background must be served when bulk depth is below threshold" );
        await testQueue.AcknowledgeAsync( phase1Msg.MessageId, TestContext.CancellationToken );

        // Phase 2: add more bulk to reach threshold
        for (int i = 0; i < BulkThreshold - (BulkThreshold - 1); i++) {
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Bulk, TestContext.CancellationToken );
        }
        // Now bulk depth = BulkThreshold; add interactive so interactive is still first
        QueuedLookupRequest interactiveRequest = CreateTestRequest( );
        await testQueue.EnqueueAsync( interactiveRequest, QueuePriority.Interactive, TestContext.CancellationToken );

        // Normal dequeue: interactive first (non-aging slot)
        QueuedMessage<QueuedLookupRequest>? phase2First = await testQueue.DequeueAsync( TestContext.CancellationToken );
        Assert.IsNotNull( phase2First );
        await testQueue.AcknowledgeAsync( phase2First.MessageId, TestContext.CancellationToken );

        // Drain remaining: with no interactive left and bulk eligible, bulk must eventually appear
        bool bulkServed = false;
        QueuedMessage<QueuedLookupRequest>? next;
        while ((next = await testQueue.DequeueAsync( TestContext.CancellationToken )) is not null) {
            if (next.MessageId.Contains( "bulk:" )) {
                bulkServed = true;
            }
            await testQueue.AcknowledgeAsync( next.MessageId, TestContext.CancellationToken );
        }
        Assert.IsTrue( bulkServed,
            "Phase 2: bulk messages must be served once depth reaches threshold" );
    }

    /// <summary>
    /// B10 — Rate-limited interactive stream must not block eligible background.
    /// Enqueues an interactive message whose lookup type is "blocked" and a background message
    /// whose lookup type is not blocked. DequeueAsync(rateLimitTracker) must skip the interactive
    /// message and return the background message.
    /// Failure-first: before the rate-limit-aware dequeue, interactive would be returned regardless.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task B10_RateLimitedInteractive_DoesNotBlockEligibleBackground( ) {
        // Arrange
        IOptions<QueueSettings> testSettings = Options.Create( new QueueSettings {
            DefaultMinBulkQueueThreshold = 0,
            InteractiveAgingInterval = 100
        } );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> testLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> testQueue = new(
            s_redis!,
            testLogger.Object,
            testSettings,
            SupportedProviders.Spotify
        );
        await testQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        // Interactive request uses IsrcLookup (will be blocked)
        string blockedEndpoint = LookupRequestType.IsrcLookup.ToString( );
        QueuedLookupRequest interactiveRequest = CreateTestRequest( lookupType: LookupRequestType.IsrcLookup );
        // Background request uses SongIdLookup (not blocked)
        QueuedLookupRequest bgRequest = CreateTestRequest( lookupType: LookupRequestType.SongIdLookup );

        await testQueue.EnqueueAsync( interactiveRequest, QueuePriority.Interactive, TestContext.CancellationToken );
        await testQueue.EnqueueAsync( bgRequest, QueuePriority.Background, TestContext.CancellationToken );

        // Mock rate limit tracker: IsrcLookup is blocked for Spotify
        Mock<BridgeBeats.Contracts.Interfaces.IRateLimitTracker> trackerMock = new( );
        _ = trackerMock
            .Setup( t => t.GetAllRateLimitedAsync( SupportedProviders.Spotify, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( (IReadOnlyList<RateLimitedEndpoint>)[
                new RateLimitedEndpoint( blockedEndpoint, DateTimeOffset.UtcNow.AddMinutes( 1 ) )
            ] );

        // Act — rate-limit-aware dequeue must skip interactive and serve background
        QueuedMessage<QueuedLookupRequest>? message = await testQueue.DequeueAsync( trackerMock.Object, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( message );
        Assert.AreEqual( bgRequest.RequestId, message.Payload.RequestId,
            "Background message must be served when the interactive message's endpoint is rate-limited" );
    }

    /// <summary>
    /// B11 — Control: interactive served first when not blocked.
    /// With both interactive and background messages present and no rate limiting,
    /// the first dequeue must return the interactive message.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task B11_Control_InteractiveServedFirstWhenNotBlocked( ) {
        // Arrange
        IOptions<QueueSettings> testSettings = Options.Create( new QueueSettings {
            DefaultMinBulkQueueThreshold = 0,
            InteractiveAgingInterval = 100
        } );
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> testLogger = new( );
        RedisRequestQueue<QueuedLookupRequest> testQueue = new(
            s_redis!,
            testLogger.Object,
            testSettings,
            SupportedProviders.Spotify
        );
        await testQueue.EnsureConsumerGroupsAsync( TestContext.CancellationToken );

        QueuedLookupRequest bgRequest = CreateTestRequest( );
        QueuedLookupRequest interactiveRequest = CreateTestRequest( );
        await testQueue.EnqueueAsync( bgRequest, QueuePriority.Background, TestContext.CancellationToken );
        await testQueue.EnqueueAsync( interactiveRequest, QueuePriority.Interactive, TestContext.CancellationToken );

        // Mock tracker: nothing rate-limited
        Mock<BridgeBeats.Contracts.Interfaces.IRateLimitTracker> trackerMock = new( );
        _ = trackerMock
            .Setup( t => t.GetAllRateLimitedAsync( SupportedProviders.Spotify, It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( (IReadOnlyList<RateLimitedEndpoint>)[] );

        // Act
        QueuedMessage<QueuedLookupRequest>? message = await testQueue.DequeueAsync( trackerMock.Object, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( message );
        Assert.AreEqual( interactiveRequest.RequestId, message.Payload.RequestId,
            "Interactive must be served first when no rate limiting is active" );
    }

    /// <summary>
    /// Creates a test <see cref="QueuedLookupRequest"/> with a unique ID for the specified provider and lookup type.
    /// </summary>
    /// <param name="provider">The music provider for the request. Defaults to Spotify.</param>
    /// <param name="lookupType">The lookup type for the request. Defaults to IsrcLookup.</param>
    /// <returns>A new <see cref="QueuedLookupRequest"/> instance.</returns>
    private static QueuedLookupRequest CreateTestRequest(
        SupportedProviders provider = SupportedProviders.Spotify,
        LookupRequestType lookupType = LookupRequestType.IsrcLookup
    ) {
        string id = Guid.NewGuid( ).ToString( "N" )[..8];
        return new QueuedLookupRequest {
            RequestId = $"req-{id}",
            Provider = provider,
            LookupType = lookupType,
            LookupValue = $"USRC{id}",
            SagaId = $"saga-{id}",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [GeneratedRegex( "^[0-9a-f]{32}$" )]
    private static partial Regex GuidHexRegex( );
}
