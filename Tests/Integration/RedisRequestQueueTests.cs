using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
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
public class RedisRequestQueueTests {

    private static IConnectionMultiplexer? s_redis;

    private Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> _mockLogger = null!;
    private IOptions<QueueSettings> _settings = null!;
    private RedisRequestQueue<QueuedLookupRequest> _queue = null!;

    /// <summary>
    /// Initializes the shared Redis connection for all tests in this class.
    /// </summary>
    /// <param name="context">The test context provided by MSTest.</param>
    [ClassInitialize]
    [Obsolete]
    public static async Task ClassInitialize( TestContext context ) {
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
        await _queue.EnsureConsumerGroupsAsync( );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.EnqueueAsync"/> adds messages to the interactive priority stream.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_AddsMessageToInteractiveStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );

        // Act
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        // Assert
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 1, depth.Interactive );
        Assert.AreEqual( 0, depth.Background );
        Assert.AreEqual( 0, depth.Bulk );
        Assert.AreEqual( 1, depth.Total );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.EnqueueAsync"/> adds messages to the background priority stream.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_AddsMessageToBackgroundStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );

        // Act
        await _queue.EnqueueAsync( request, QueuePriority.Background );

        // Assert
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 0, depth.Interactive );
        Assert.AreEqual( 1, depth.Background );
        Assert.AreEqual( 0, depth.Bulk );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.EnqueueAsync"/> adds messages to the bulk priority stream.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_AddsMessageToBulkStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );

        // Act
        await _queue.EnqueueAsync( request, QueuePriority.Bulk );

        // Assert
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 0, depth.Interactive );
        Assert.AreEqual( 0, depth.Background );
        Assert.AreEqual( 1, depth.Bulk );
    }

    /// <summary>
    /// Verifies that DequeueAsync returns a message when one is available.
    /// </summary>
    [TestMethod]
    public async Task DequeueAsync_ReturnsMessage_WhenAvailable( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        // Act
        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );

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
    public async Task DequeueAsync_ReturnsNull_WhenQueueEmpty( ) {
        // Act
        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );

        // Assert
        Assert.IsNull( message );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.AcknowledgeAsync"/> removes the message from the stream.
    /// </summary>
    [TestMethod]
    public async Task AcknowledgeAsync_RemovesMessageFromStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );
        Assert.IsNotNull( message );

        // Act
        await _queue.AcknowledgeAsync( message.MessageId );

        // Assert - queue should be empty after ack
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 0, depth.Total );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.MoveToDlqAsync"/> moves the message to the dead letter queue.
    /// </summary>
    [TestMethod]
    public async Task MoveToDlqAsync_MovesMessageToDlq( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );
        Assert.IsNotNull( message );

        // Act
        await _queue.MoveToDlqAsync( message.MessageId, "Test failure reason" );

        // Assert - main queue empty, DLQ has message
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 0, depth.Total );

        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> dlqMessages = await _queue.GetDlqMessagesAsync( 10 );
        Assert.HasCount( 1, dlqMessages );
        Assert.AreEqual( request.RequestId, dlqMessages[0].Payload.RequestId );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.RequeueFromDlqAsync"/> moves the message back from DLQ to the main queue.
    /// </summary>
    [TestMethod]
    public async Task RequeueFromDlqAsync_MovesMessageBackToQueue( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );
        Assert.IsNotNull( message );

        await _queue.MoveToDlqAsync( message.MessageId, "Test failure" );

        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> dlqMessages = await _queue.GetDlqMessagesAsync( 10 );
        Assert.HasCount( 1, dlqMessages );

        // Act
        await _queue.RequeueFromDlqAsync( dlqMessages[0].MessageId, QueuePriority.Background );

        // Assert - DLQ empty, background queue has message
        dlqMessages = await _queue.GetDlqMessagesAsync( 10 );
        Assert.IsEmpty( dlqMessages );

        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 1, depth.Background );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.DeleteFromDlqAsync"/> permanently removes the message from the DLQ.
    /// </summary>
    [TestMethod]
    public async Task DeleteFromDlqAsync_RemovesMessageFromDlq( ) {
        // Arrange - use Interactive priority to avoid bulk gating
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );
        Assert.IsNotNull( message );

        await _queue.MoveToDlqAsync( message.MessageId, "Test failure" );

        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> dlqMessages = await _queue.GetDlqMessagesAsync( 10 );
        Assert.HasCount( 1, dlqMessages );

        // Act
        bool deleted = await _queue.DeleteFromDlqAsync( dlqMessages[0].MessageId );

        // Assert
        Assert.IsTrue( deleted );

        dlqMessages = await _queue.GetDlqMessagesAsync( 10 );
        Assert.IsEmpty( dlqMessages );
    }

    /// <summary>
    /// Verifies that queues for different providers are isolated and do not share messages.
    /// </summary>
    [TestMethod]
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
        await appleQueue.EnsureConsumerGroupsAsync( );

        QueuedLookupRequest spotifyRequest = CreateTestRequest( provider: SupportedProviders.Spotify );
        QueuedLookupRequest appleRequest = CreateTestRequest( provider: SupportedProviders.AppleMusic );

        // Act
        await spotifyQueue.EnqueueAsync( spotifyRequest, QueuePriority.Interactive );
        await appleQueue.EnqueueAsync( appleRequest, QueuePriority.Interactive );

        // Assert - each queue has its own message
        QueueDepth spotifyDepth = await spotifyQueue.GetDepthAsync( );
        QueueDepth appleDepth = await appleQueue.GetDepthAsync( );

        Assert.AreEqual( 1, spotifyDepth.Interactive );
        Assert.AreEqual( 1, appleDepth.Interactive );

        // Dequeue from Spotify should get Spotify request
        QueuedMessage<QueuedLookupRequest>? spotifyMessage = await spotifyQueue.DequeueAsync( );
        Assert.IsNotNull( spotifyMessage );
        Assert.AreEqual( SupportedProviders.Spotify, spotifyMessage.Payload.Provider );

        // Dequeue from Apple should get Apple request
        QueuedMessage<QueuedLookupRequest>? appleMessage = await appleQueue.DequeueAsync( );
        Assert.IsNotNull( appleMessage );
        Assert.AreEqual( SupportedProviders.AppleMusic, appleMessage.Payload.Provider );
    }

    /// <summary>
    /// Verifies that weighted priority selection processes all messages across all priority levels.
    /// </summary>
    [TestMethod]
    public async Task WeightedPrioritySelection_ProcessesAllMessages( ) {
        // Arrange - add multiple messages to each priority
        const int messagesPerPriority = 10;

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
        await testQueue.EnsureConsumerGroupsAsync( );

        for (int i = 0; i < messagesPerPriority; i++) {
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Interactive );
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Background );
            await testQueue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Bulk );
        }

        // Act - dequeue all and track which priorities are drained first
        // With weighted selection, all messages should eventually be processed
        int totalDequeued = 0;

        QueuedMessage<QueuedLookupRequest>? message;
        while ((message = await testQueue.DequeueAsync( )) is not null) {
            await testQueue.AcknowledgeAsync( message.MessageId );
            totalDequeued++;
        }

        // Assert - all messages should be processed
        Assert.AreEqual( messagesPerPriority * 3, totalDequeued, "All messages should be dequeued" );

        QueueDepth depth = await testQueue.GetDepthAsync( );
        Assert.AreEqual( 0, depth.Total, "Queue should be empty after processing all messages" );
    }

    /// <summary>
    /// Verifies that Redis consumer groups allow multiple worker instances to process messages without duplication.
    /// </summary>
    [TestMethod]
    public async Task ConsumerGroups_AllowMultipleWorkerInstances( ) {
        // Arrange - create two queue instances (simulating two workers)
        Mock<ILogger<RedisRequestQueue<QueuedLookupRequest>>> logger2 = new( );
        RedisRequestQueue<QueuedLookupRequest> worker2Queue = new(
            s_redis!,
            logger2.Object,
            _settings,
            SupportedProviders.Spotify
        );
        await worker2Queue.EnsureConsumerGroupsAsync( );

        // Enqueue multiple messages
        const int messageCount = 10;
        for (int i = 0; i < messageCount; i++) {
            await _queue.EnqueueAsync( CreateTestRequest( ), QueuePriority.Interactive );
        }

        // Act - both workers dequeue concurrently
        int worker1Count = 0;
        int worker2Count = 0;

        for (int i = 0; i < messageCount; i++) {
            // Alternate between workers
            if (i % 2 == 0) {
                QueuedMessage<QueuedLookupRequest>? msg = await _queue.DequeueAsync( );
                if (msg is not null) {
                    worker1Count++;
                    await _queue.AcknowledgeAsync( msg.MessageId );
                }
            } else {
                QueuedMessage<QueuedLookupRequest>? msg = await worker2Queue.DequeueAsync( );
                if (msg is not null) {
                    worker2Count++;
                    await worker2Queue.AcknowledgeAsync( msg.MessageId );
                }
            }
        }

        // Assert - both workers processed messages, no duplicates
        Assert.AreEqual( messageCount, worker1Count + worker2Count );
        Assert.IsGreaterThan( 0, worker1Count, "Worker 1 should have processed at least one message" );
        Assert.IsGreaterThan( 0, worker2Count, "Worker 2 should have processed at least one message" );

        // Queue should be empty
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 0, depth.Total );
    }

    /// <summary>
    /// Verifies that <see cref="RedisRequestQueue{T}.RequeueAsync"/> puts the message back in the queue for retry.
    /// </summary>
    [TestMethod]
    public async Task RequeueAsync_PutsMessageBackForRetry( ) {
        // Arrange
        QueuedLookupRequest request = CreateTestRequest( );
        await _queue.EnqueueAsync( request, QueuePriority.Interactive );

        QueuedMessage<QueuedLookupRequest>? message = await _queue.DequeueAsync( );
        Assert.IsNotNull( message );

        // Simulate temporary failure - requeue with delay
        await _queue.RequeueAsync( message.MessageId, TimeSpan.FromMilliseconds( 100 ) );

        // Assert - message should be back in queue
        QueueDepth depth = await _queue.GetDepthAsync( );
        Assert.AreEqual( 1, depth.Total );

        // Should be able to dequeue again
        QueuedMessage<QueuedLookupRequest>? requeued = await _queue.DequeueAsync( );
        Assert.IsNotNull( requeued );
        Assert.AreEqual( request.RequestId, requeued.Payload.RequestId );
    }

    /// <summary>
    /// Creates a test <see cref="QueuedLookupRequest"/> with a unique ID for the specified provider.
    /// </summary>
    /// <param name="provider">The music provider for the request. Defaults to Spotify.</param>
    /// <returns>A new <see cref="QueuedLookupRequest"/> instance.</returns>
    private static QueuedLookupRequest CreateTestRequest( SupportedProviders provider = SupportedProviders.Spotify ) {
        string id = Guid.NewGuid( ).ToString( "N" )[..8];
        return new QueuedLookupRequest {
            RequestId = $"req-{id}",
            Provider = provider,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = $"USRC{id}",
            SagaId = $"saga-{id}",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
