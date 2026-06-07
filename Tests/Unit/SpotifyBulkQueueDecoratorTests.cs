using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Queue;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SpotifyBulkQueueDecorator"/> to verify the enqueue
/// routing seam: SongIdLookup/AlbumIdLookup are written directly to the
/// type-specific bulk streams, while all other lookup types delegate to the
/// inner queue.
/// </summary>
[TestClass]
public class SpotifyBulkQueueDecoratorTests {

    private Mock<IRequestQueue<QueuedLookupRequest>> _innerQueueMock = null!;
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _databaseMock = null!;
    private Mock<ILogger<SpotifyBulkQueueDecorator>> _loggerMock = null!;
    private SpotifyBulkQueueDecorator _decorator = null!;

    private const string BulkTrackIdStream = "queue:spotify:bulk:track-id";
    private const string BulkAlbumIdStream = "queue:spotify:bulk:album-id";

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Gets or sets the test context for the current test.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Initializes mocks before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _innerQueueMock = new Mock<IRequestQueue<QueuedLookupRequest>>( );
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _databaseMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<SpotifyBulkQueueDecorator>>( );

        _ = _redisMock.Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _databaseMock.Object );

        // Default: any StreamAddAsync call succeeds (8-param overload used by StackExchange.Redis 2.13)
        _ = _databaseMock
            .Setup( d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)"1234567890-0" );

        _decorator = new SpotifyBulkQueueDecorator( _innerQueueMock.Object, _redisMock.Object, _loggerMock.Object );
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that the constructor throws when inner queue is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullInnerQueue_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyBulkQueueDecorator( null!, _redisMock.Object, _loggerMock.Object )
        );
        Assert.AreEqual( "inner", ex.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws when redis is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullRedis_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyBulkQueueDecorator( _innerQueueMock.Object, null!, _loggerMock.Object )
        );
        Assert.AreEqual( "redis", ex.ParamName );
    }

    /// <summary>
    /// Verifies that the constructor throws when logger is null.
    /// </summary>
    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException( ) {
        // Act & Assert
        ArgumentNullException ex = Assert.ThrowsExactly<ArgumentNullException>(
            ( ) => _ = new SpotifyBulkQueueDecorator( _innerQueueMock.Object, _redisMock.Object, null! )
        );
        Assert.AreEqual( "logger", ex.ParamName );
    }

    #endregion

    #region Enqueue Routing Tests

    /// <summary>
    /// Verifies that SongIdLookup is written to the track-id bulk stream, not the inner queue.
    /// Failure-first: before §1.2, SongIdLookup was forwarded to inner queue (UriLookup path);
    /// this test verifies the XADD interception added in §1.2.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenSongIdLookup_ShouldWriteToTrackIdStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, "3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert — XADD was called exactly once, with the track-id stream key
        _databaseMock.Verify(
            d => d.StreamAddAsync(
                BulkTrackIdStream,
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Once
        );

        // Assert — inner queue was NOT called
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that AlbumIdLookup is written to the album-id bulk stream, not the inner queue.
    /// Failure-first: before §1.2, AlbumIdLookup was forwarded to inner queue;
    /// this test verifies the XADD interception added in §1.2.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenAlbumIdLookup_ShouldWriteToAlbumIdStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateRequest( LookupRequestType.AlbumIdLookup, "6WdSsBrH5QtofaTTqgwxOV" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert — XADD was called exactly once, with the album-id stream key
        _databaseMock.Verify(
            d => d.StreamAddAsync(
                BulkAlbumIdStream,
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Once
        );

        // Assert — inner queue was NOT called
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that UriLookup is forwarded to the inner queue, not written to any stream.
    /// Failure-first: the pre-§1.2 state forwarded everything; this test verifies the passthrough
    /// path is preserved for UriLookup after the interception for typed IDs is added.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenUriLookup_ShouldDelegateToInnerQueue( ) {
        // Arrange
        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        QueuedLookupRequest request = CreateRequest( LookupRequestType.UriLookup, "https://open.spotify.com/artist/6qqNVTkY8uBg9cP3Jd7DAH" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert — inner queue was called once with the original request
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Bulk, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert — no XADD to any stream
        _databaseMock.Verify(
            d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Never
        );
    }

    /// <summary>
    /// Verifies that IsrcLookup is forwarded to the inner queue.
    /// Ensures non-ID lookup types are not accidentally intercepted.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenIsrcLookup_ShouldDelegateToInnerQueue( ) {
        // Arrange
        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        QueuedLookupRequest request = CreateRequest( LookupRequestType.IsrcLookup, "USRC17607839" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert — delegates, does not write to stream
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Bulk, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );
    }

    /// <summary>
    /// Verifies that the payload field in the XADD call is valid JSON containing the request.
    /// Failure-first: before §1.2 the payload field was never set; this verifies the serialized
    /// request written to the stream matches the original request.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenSongIdLookup_ShouldSerializePayloadField( ) {
        // Arrange
        NameValueEntry[]? capturedFields = null;
        _ = _databaseMock
            .Setup( d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, NameValueEntry[] fields, RedisValue? _, long? _, bool _, long? _, StreamTrimMode _, CommandFlags _ ) => {
                capturedFields = fields;
            } )
            .ReturnsAsync( (RedisValue)"1234567890-0" );

        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, "3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert — fields contain "payload" and "enqueuedAt"
        Assert.IsNotNull( capturedFields );
        string? payloadField = (string?)capturedFields.FirstOrDefault( f => f.Name == "payload" ).Value;
        string? enqueuedAtField = (string?)capturedFields.FirstOrDefault( f => f.Name == "enqueuedAt" ).Value;
        Assert.IsNotNull( payloadField, "payload field must be present" );
        Assert.IsNotNull( enqueuedAtField, "enqueuedAt field must be present" );

        // Payload must deserialize back to the original request
        QueuedLookupRequest? deserialized = JsonSerializer.Deserialize<QueuedLookupRequest>( payloadField, s_jsonOptions );
        Assert.IsNotNull( deserialized );
        Assert.AreEqual( request.LookupType, deserialized.LookupType );
        Assert.AreEqual( request.LookupValue, deserialized.LookupValue );
        Assert.AreEqual( request.SagaId, deserialized.SagaId );
    }

    /// <summary>
    /// Verifies that non-enqueue operations (Dequeue, Acknowledge, GetDepth) are always delegated.
    /// These operations have no stream-specific override in the decorator.
    /// </summary>
    [TestMethod]
    public async Task NonEnqueueOperations_ShouldAlwaysDelegateToInnerQueue( ) {
        // Arrange — set up inner queue stubs
        _ = _innerQueueMock
            .Setup( q => q.DequeueAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( (QueuedMessage<QueuedLookupRequest>?)null );
        _ = _innerQueueMock
            .Setup( q => q.AcknowledgeAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );
        _ = _innerQueueMock
            .Setup( q => q.GetDepthAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( new QueueDepth( 0, 0, 0, 0 ) );

        // Act
        _ = await _decorator.DequeueAsync( TestContext.CancellationToken );
        await _decorator.AcknowledgeAsync( "msg-1", TestContext.CancellationToken );
        _ = await _decorator.GetDepthAsync( TestContext.CancellationToken );

        // Assert — each delegated to inner
        _innerQueueMock.Verify( q => q.DequeueAsync( It.IsAny<CancellationToken>( ) ), Times.Once );
        _innerQueueMock.Verify( q => q.AcknowledgeAsync( "msg-1", It.IsAny<CancellationToken>( ) ), Times.Once );
        _innerQueueMock.Verify( q => q.GetDepthAsync( It.IsAny<CancellationToken>( ) ), Times.Once );
    }

    #endregion

    #region Helpers

    private static QueuedLookupRequest CreateRequest( LookupRequestType lookupType, string lookupValue ) =>
        new( ) {
            RequestId = Guid.NewGuid( ).ToString( "N" ),
            Provider = SupportedProviders.Spotify,
            LookupType = lookupType,
            LookupValue = lookupValue,
            SagaId = Guid.NewGuid( ).ToString( "N" ),
            IsAlbum = lookupType == LookupRequestType.AlbumIdLookup,
            OriginPriority = QueuePriority.Bulk
        };

    #endregion
}
