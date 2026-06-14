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
/// Unit tests for <see cref="SpotifyBulkQueueDecorator"/>, which intercepts enqueue calls and routes
/// Spotify song-id and album-id lookups into dedicated bulk Redis streams while delegating
/// everything else to the wrapped inner queue. Covers constructor null-guards, the routing matrix
/// (song-id to the track stream, album-id to the album stream, URI and ISRC to the inner queue, and
/// interactive-priority typed lookups back to the inner queue regardless of type), the stream
/// payload shape (a serialized <c>payload</c> field plus an <c>enqueuedAt</c> field), and the rule
/// that non-enqueue operations always delegate to the inner queue.
/// </summary>
[TestClass]
public class SpotifyBulkQueueDecoratorTests {

    /// <summary>Mock inner queue the decorator wraps and delegates to.</summary>
    private Mock<IRequestQueue<QueuedLookupRequest>> _innerQueueMock = null!;
    /// <summary>Mock Redis multiplexer supplying the database for bulk-stream writes.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock Redis database the decorator writes bulk-stream entries to.</summary>
    private Mock<IDatabase> _databaseMock = null!;
    /// <summary>Mock logger for the decorator.</summary>
    private Mock<ILogger<SpotifyBulkQueueDecorator>> _loggerMock = null!;
    /// <summary>The decorator under test.</summary>
    private SpotifyBulkQueueDecorator _decorator = null!;

    /// <summary>Redis stream key for bulk Spotify track-id lookups.</summary>
    private const string BulkTrackIdStream = "queue:spotify:bulk:track-id";
    /// <summary>Redis stream key for bulk Spotify album-id lookups.</summary>
    private const string BulkAlbumIdStream = "queue:spotify:bulk:album-id";

    /// <summary>
    /// Serialization options (camel-case, non-indented) used to deserialize the captured
    /// <c>payload</c> field and assert its contents match the enqueued request.
    /// </summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>MSTest-injected context; its cancellation token is passed to enqueue calls.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates fresh mocks before each test, wires the Redis database, defaults stream-add to a
    /// fixed id, and constructs the decorator.
    /// </summary>
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
    /// Verifies that a null inner queue throws <see cref="ArgumentNullException"/> with parameter
    /// name <c>inner</c>.
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
    /// Verifies that a null Redis multiplexer throws <see cref="ArgumentNullException"/> with
    /// parameter name <c>redis</c>.
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
    /// Verifies that a null logger throws <see cref="ArgumentNullException"/> with parameter name
    /// <c>logger</c>.
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
    /// Verifies that a bulk-priority song-id lookup is written to the track-id stream and not
    /// delegated to the inner queue.
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
    /// Verifies that a bulk-priority album-id lookup is written to the album-id stream and not
    /// delegated to the inner queue.
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
    /// Verifies that a URI lookup is delegated to the inner queue and never written to a bulk
    /// stream, since it is not a typed Spotify id lookup.
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
    /// Verifies that an ISRC lookup is delegated to the inner queue rather than routed to a bulk
    /// stream.
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
    /// Verifies that a song-id lookup at interactive priority bypasses the bulk stream and delegates
    /// to the inner queue, so interactive callers are not subject to the bulk stream's linger.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenSongIdLookupAndInteractivePriority_ShouldDelegateToInnerQueue( ) {
        // Arrange
        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, "3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — inner queue receives the call exactly once at Interactive priority
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert — no StreamAddAsync (no bulk-stream write)
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
    /// Verifies that an album-id lookup at interactive priority likewise bypasses the bulk stream
    /// and delegates to the inner queue.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenAlbumIdLookupAndInteractivePriority_ShouldDelegateToInnerQueue( ) {
        // Arrange
        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        QueuedLookupRequest request = CreateRequest( LookupRequestType.AlbumIdLookup, "6WdSsBrH5QtofaTTqgwxOV" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — inner queue receives the call exactly once at Interactive priority
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Once
        );

        // Assert — no StreamAddAsync (no bulk-stream write)
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
    /// Verifies that a song-id lookup at background priority is routed to the track-id stream:
    /// only interactive priority bypasses the bulk stream, so background still batches.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenSongIdLookupAndBackgroundPriority_ShouldWriteToTrackIdStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateRequest( LookupRequestType.SongIdLookup, "3n3Ppam7vgaVa1iaRUc9Lp" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Background, TestContext.CancellationToken );

        // Assert — XADD to the track-id stream
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
    /// Verifies that an album-id lookup at background priority is routed to the album-id stream,
    /// mirroring the song-id background-priority routing.
    /// </summary>
    [TestMethod]
    public async Task EnqueueAsync_WhenAlbumIdLookupAndBackgroundPriority_ShouldWriteToAlbumIdStream( ) {
        // Arrange
        QueuedLookupRequest request = CreateRequest( LookupRequestType.AlbumIdLookup, "6WdSsBrH5QtofaTTqgwxOV" );

        // Act
        await _decorator.EnqueueAsync( request, QueuePriority.Background, TestContext.CancellationToken );

        // Assert — XADD to the album-id stream
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
    /// Verifies the bulk-stream entry shape for a song-id lookup: the written fields include a
    /// <c>payload</c> field whose JSON deserializes back to the request (lookup type, value, saga
    /// id) and an <c>enqueuedAt</c> field carrying the enqueue timestamp.
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
    /// Verifies that non-enqueue operations (dequeue, acknowledge, get-depth) always delegate to the
    /// inner queue: the decorator only specializes enqueue routing, not consumption.
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

    /// <summary>
    /// Builds a Spotify <c>QueuedLookupRequest</c> of the given type and value, with a fresh request
    /// and saga id and <c>IsAlbum</c> inferred from the lookup type.
    /// </summary>
    /// <param name="lookupType">The lookup type that drives routing under test.</param>
    /// <param name="lookupValue">The id or URL being looked up.</param>
    /// <returns>A bulk-priority request ready to enqueue through the decorator.</returns>
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
