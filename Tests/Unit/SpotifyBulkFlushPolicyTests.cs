using BridgeBeats.Contracts.Constants;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for the Spotify bulk-flush policy: the queue-depth and oldest-age reads on
/// <see cref="SpotifyBatchQueueHelper"/> that feed the flush decision, and the pure
/// <c>SpotifyBulkProcessorService.ShouldFlush</c> predicate. Encodes the flush invariants — a size
/// trigger when the queued count reaches the batch threshold, an age trigger when the oldest entry's
/// age reaches the linger (an inclusive <c>&gt;=</c> boundary), no flush on an empty queue or a
/// missing oldest-age timestamp — and confirms the depth/age reads return per-type counts and the
/// recorded <c>enqueuedAt</c> timestamp.
/// </summary>
[TestClass]
public class SpotifyBulkFlushPolicyTests {

    /// <summary>Mock Redis multiplexer supplying the database the helper reads stream metadata from.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock Redis database backing stream length and range reads.</summary>
    private Mock<IDatabase> _databaseMock = null!;
    /// <summary>Mock logger for the helper.</summary>
    private Mock<ILogger<SpotifyBatchQueueHelper>> _loggerMock = null!;
    /// <summary>The batch queue helper under test.</summary>
    private SpotifyBatchQueueHelper _helper = null!;

    /// <summary>Redis stream key for bulk Spotify track-id lookups.</summary>
    private const string BulkTrackIdStream = "queue:spotify:bulk:track-id";
    /// <summary>Redis stream key for bulk Spotify album-id lookups.</summary>
    private const string BulkAlbumIdStream = "queue:spotify:bulk:album-id";

    /// <summary>MSTest-injected context; its cancellation token bounds the helper's async reads.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates fresh mocks before each test, defaults all stream lengths to zero and all stream
    /// ranges to empty, and constructs the helper.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _databaseMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<SpotifyBatchQueueHelper>>( );

        _ = _redisMock.Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _databaseMock.Object );

        // Default: all StreamLength calls return 0
        _ = _databaseMock.Setup( d => d.StreamLengthAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 0L );

        // Default: StreamRangeAsync returns empty array (no messages in any stream)
        _ = _databaseMock.Setup( d => d.StreamRangeAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        _helper = new SpotifyBatchQueueHelper( _redisMock.Object, _loggerMock.Object );
    }

    #region GetIdLookupDepthAsync — BulkTrackIdCount / BulkAlbumIdCount

    /// <summary>
    /// Verifies that <c>GetIdLookupDepthAsync</c> reports the track-id stream length as
    /// <c>BulkTrackIdCount</c>.
    /// </summary>
    [TestMethod]
    public async Task GetIdLookupDepthAsync_WhenTrackIdStreamHasMessages_ShouldReturnBulkTrackIdCount( ) {
        // Arrange — 5 messages in the type-specific track stream
        _ = _databaseMock.Setup( d => d.StreamLengthAsync( BulkTrackIdStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 5L );

        // Act
        IdLookupDepth depth = await _helper.GetIdLookupDepthAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( 5, depth.BulkTrackIdCount,
            "BulkTrackIdCount must equal the type-specific track-id stream length" );
    }

    /// <summary>
    /// Verifies that <c>GetIdLookupDepthAsync</c> reports the album-id stream length as
    /// <c>BulkAlbumIdCount</c>.
    /// </summary>
    [TestMethod]
    public async Task GetIdLookupDepthAsync_WhenAlbumIdStreamHasMessages_ShouldReturnBulkAlbumIdCount( ) {
        // Arrange — 3 messages in the type-specific album stream
        _ = _databaseMock.Setup( d => d.StreamLengthAsync( BulkAlbumIdStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 3L );

        // Act
        IdLookupDepth depth = await _helper.GetIdLookupDepthAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( 3, depth.BulkAlbumIdCount,
            "BulkAlbumIdCount must equal the type-specific album-id stream length" );
    }

    /// <summary>
    /// Verifies that when the track-id stream sits at the maximum batch size, the reported
    /// <c>BulkTrackIdCount</c> reflects that threshold value exactly.
    /// </summary>
    [TestMethod]
    public async Task GetIdLookupDepthAsync_WhenTrackIdStreamAtThreshold_ShouldReflectMaxCount( ) {
        // Arrange — exactly MaxTracksPerBatchLookup messages
        _ = _databaseMock.Setup( d => d.StreamLengthAsync( BulkTrackIdStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( SpotifyConstants.MaxTracksPerBatchLookup );

        // Act
        IdLookupDepth depth = await _helper.GetIdLookupDepthAsync( TestContext.CancellationToken );

        // Assert — matches the threshold that triggers an immediate size flush
        Assert.AreEqual( SpotifyConstants.MaxTracksPerBatchLookup, depth.BulkTrackIdCount );
    }

    /// <summary>
    /// Verifies that when every stream is empty, all four depth counters (bulk track-id, bulk
    /// album-id, track-id, album-id) read zero.
    /// </summary>
    [TestMethod]
    public async Task GetIdLookupDepthAsync_WhenAllStreamsEmpty_ShouldReturnZeroCounts( ) {
        // Arrange — default setup: all StreamLength = 0, all StreamRange = empty

        // Act
        IdLookupDepth depth = await _helper.GetIdLookupDepthAsync( TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( 0, depth.BulkTrackIdCount );
        Assert.AreEqual( 0, depth.BulkAlbumIdCount );
        Assert.AreEqual( 0, depth.TrackIdCount );
        Assert.AreEqual( 0, depth.AlbumIdCount );
    }

    #endregion

    #region GetOldestEnqueuedAtAsync — age-based flush trigger

    /// <summary>
    /// Verifies that <c>GetOldestEnqueuedAtAsync</c> returns null when the stream is empty, so the
    /// age trigger cannot fire on an empty queue.
    /// </summary>
    [TestMethod]
    public async Task GetOldestEnqueuedAtAsync_WhenStreamIsEmpty_ShouldReturnNull( ) {
        // Arrange — StreamRangeAsync returns no entries (default)

        // Act
        DateTimeOffset? oldest = await _helper.GetOldestEnqueuedAtAsync( isTracks: true, TestContext.CancellationToken );

        // Assert
        Assert.IsNull( oldest,
            "Should return null when the stream is empty" );
    }

    /// <summary>
    /// Verifies that for the track stream, <c>GetOldestEnqueuedAtAsync</c> returns the
    /// <c>enqueuedAt</c> timestamp of the oldest entry.
    /// </summary>
    [TestMethod]
    public async Task GetOldestEnqueuedAtAsync_WhenTrackStreamHasEntry_ShouldReturnEnqueuedAtTimestamp( ) {
        // Arrange — one entry in the track-id stream with a known enqueuedAt value
        DateTimeOffset expected = DateTimeOffset.UtcNow.AddSeconds( -10 );
        StreamEntry fakeEntry = CreateStreamEntryWithEnqueuedAt( expected );

        _ = _databaseMock.Setup( d => d.StreamRangeAsync(
                BulkTrackIdStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                1,
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [fakeEntry] );

        // Act
        DateTimeOffset? oldest = await _helper.GetOldestEnqueuedAtAsync( isTracks: true, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( oldest );
        Assert.AreEqual( expected.ToString( "O" ), oldest.Value.ToString( "O" ),
            "Returned timestamp must match the enqueuedAt field in the oldest stream entry" );
    }

    /// <summary>
    /// Verifies that for the album stream (selected via <c>isTracks: false</c>),
    /// <c>GetOldestEnqueuedAtAsync</c> returns the oldest entry's <c>enqueuedAt</c> timestamp.
    /// </summary>
    [TestMethod]
    public async Task GetOldestEnqueuedAtAsync_WhenAlbumStreamHasEntry_ShouldReturnEnqueuedAtTimestamp( ) {
        // Arrange
        DateTimeOffset expected = DateTimeOffset.UtcNow.AddSeconds( -5 );
        StreamEntry fakeEntry = CreateStreamEntryWithEnqueuedAt( expected );

        _ = _databaseMock.Setup( d => d.StreamRangeAsync(
                BulkAlbumIdStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                1,
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [fakeEntry] );

        // Act
        DateTimeOffset? oldest = await _helper.GetOldestEnqueuedAtAsync( isTracks: false, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( oldest );
        Assert.AreEqual( expected.ToString( "O" ), oldest.Value.ToString( "O" ) );
    }

    /// <summary>
    /// Verifies that when the oldest entry carries no <c>enqueuedAt</c> field,
    /// <c>GetOldestEnqueuedAtAsync</c> returns null, so a malformed entry degrades safely rather
    /// than forcing a flush.
    /// </summary>
    [TestMethod]
    public async Task GetOldestEnqueuedAtAsync_WhenEnqueuedAtFieldAbsent_ShouldReturnNull( ) {
        // Arrange — entry exists but has no enqueuedAt field
        StreamEntry entryWithoutTimestamp = CreateStreamEntryWithoutEnqueuedAt( );

        _ = _databaseMock.Setup( d => d.StreamRangeAsync(
                BulkTrackIdStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                1,
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [entryWithoutTimestamp] );

        // Act
        DateTimeOffset? oldest = await _helper.GetOldestEnqueuedAtAsync( isTracks: true, TestContext.CancellationToken );

        // Assert — graceful null when field is missing
        Assert.IsNull( oldest );
    }

    #endregion

    #region ShouldFlush predicate — direct tests of the extracted pure function

    /// <summary>
    /// Verifies that <c>ShouldFlush</c> returns true when the count reaches the size threshold, even
    /// with no oldest-age timestamp: the size trigger fires on its own.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenCountAtSizeThreshold_ShouldReturnTrue( ) {
        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: SpotifyConstants.MaxTracksPerBatchLookup,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: TimeSpan.FromMilliseconds( 500 )
        );
        Assert.IsTrue( result, "count == MaxTracksPerBatchLookup must trigger a size flush" );
    }

    /// <summary>
    /// Verifies that <c>ShouldFlush</c> returns true when the count is below the size threshold but
    /// the oldest entry's age exceeds the linger: the age trigger fires.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenCountBelowThresholdAndAgeExceedsLinger_ShouldReturnTrue( ) {
        TimeSpan linger = TimeSpan.FromMilliseconds( 500 );

        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: linger + TimeSpan.FromMilliseconds( 100 ), // 600ms > 500ms linger
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: linger
        );
        Assert.IsTrue( result, "1 message with age > linger must trigger an age flush" );
    }

    /// <summary>
    /// Verifies that <c>ShouldFlush</c> returns false when the count is below the threshold and the
    /// oldest entry's age is below the linger: a fresh, small batch is held.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenCountBelowThresholdAndAgeBelowLinger_ShouldReturnFalse( ) {
        TimeSpan linger = TimeSpan.FromMilliseconds( 500 );

        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: TimeSpan.FromMilliseconds( 50 ), // 50ms < 500ms linger
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: linger
        );
        Assert.IsFalse( result, "Fresh message (age < linger) must not trigger a flush" );
    }

    /// <summary>
    /// Verifies that <c>ShouldFlush</c> returns false when the count is zero regardless of age: an
    /// empty queue never flushes.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenCountIsZero_ShouldReturnFalse( ) {
        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 0,
            oldestAge: TimeSpan.FromSeconds( 999 ),
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: TimeSpan.FromMilliseconds( 500 )
        );
        Assert.IsFalse( result, "Empty stream (count=0) must never trigger a flush" );
    }

    /// <summary>
    /// Verifies that <c>ShouldFlush</c> returns false when the count is below the threshold and the
    /// oldest age is null: with no timestamp there is nothing to linger-flush on.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenOldestAgeIsNull_ShouldReturnFalse( ) {
        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 3,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: TimeSpan.FromMilliseconds( 500 )
        );
        Assert.IsFalse( result, "Cannot linger-flush without an oldest-age timestamp" );
    }

    /// <summary>
    /// Verifies the inclusive age boundary at the default linger: an entry aged exactly
    /// <c>DefaultLingerMs</c> flushes, while one a millisecond under does not, confirming the age
    /// trigger uses <c>&gt;=</c> rather than strict <c>&gt;</c>.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_AtDefaultLingerMs_FlushesWhenAgeEqualsLinger_NotJustUnder( ) {
        TimeSpan defaultLinger = TimeSpan.FromMilliseconds( SpotifyBatchSettings.DefaultLingerMs );

        // Exactly at the linger boundary — must flush
        bool atLinger = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: defaultLinger,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: defaultLinger
        );
        Assert.IsTrue( atLinger, "A message aged exactly DefaultLingerMs must trigger a flush" );

        // 1 ms under the linger boundary — must NOT flush
        bool justUnder = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: defaultLinger - TimeSpan.FromMilliseconds( 1 ),
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: defaultLinger
        );
        Assert.IsFalse( justUnder, "A message 1 ms under DefaultLingerMs must not trigger a flush" );
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Builds a stream entry carrying a minimal <c>payload</c> field and an <c>enqueuedAt</c> field
    /// set to <paramref name="enqueuedAt"/>, for the oldest-age read tests.
    /// </summary>
    /// <param name="enqueuedAt">The enqueue timestamp to embed.</param>
    /// <returns>A stream entry with a populated <c>enqueuedAt</c> field.</returns>
    private static StreamEntry CreateStreamEntryWithEnqueuedAt( DateTimeOffset enqueuedAt ) {
        NameValueEntry[] values = [
            new NameValueEntry( "payload", "{\"lookupType\":0}" ),
            new NameValueEntry( "enqueuedAt", enqueuedAt.ToString( "O" ) )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", values );
    }

    /// <summary>
    /// Builds a stream entry with only a <c>payload</c> field and no <c>enqueuedAt</c>, exercising
    /// the missing-timestamp degradation path.
    /// </summary>
    /// <returns>A stream entry without an <c>enqueuedAt</c> field.</returns>
    private static StreamEntry CreateStreamEntryWithoutEnqueuedAt( ) {
        NameValueEntry[] values = [
            new NameValueEntry( "payload", "{\"lookupType\":0}" )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", values );
    }

    #endregion
}
