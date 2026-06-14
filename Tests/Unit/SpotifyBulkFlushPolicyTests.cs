using BridgeBeats.Contracts.Constants;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for the size-OR-age flush policy.
/// Exercises <see cref="SpotifyBatchQueueHelper.GetIdLookupDepthAsync"/> and
/// <see cref="SpotifyBatchQueueHelper.GetOldestEnqueuedAtAsync"/> — the two methods
/// that <c>SpotifyBulkProcessorService.ShouldProcessBulkTracksAsync</c> /
/// <c>ShouldProcessBulkAlbumsAsync</c> call to make the flush decision.
/// </summary>
[TestClass]
public class SpotifyBulkFlushPolicyTests {

    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _databaseMock = null!;
    private Mock<ILogger<SpotifyBatchQueueHelper>> _loggerMock = null!;
    private SpotifyBatchQueueHelper _helper = null!;

    private const string BulkTrackIdStream = "queue:spotify:bulk:track-id";
    private const string BulkAlbumIdStream = "queue:spotify:bulk:album-id";

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Initializes mocks before each test.</summary>
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
    /// Verifies that BulkTrackIdCount reflects only the type-specific track-id stream,
    /// not the generic priority streams.
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
    /// Verifies that BulkAlbumIdCount reflects only the type-specific album-id stream.
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
    /// Verifies that BulkTrackIdCount equals exactly the max threshold (50) when 50 messages are
    /// in the track-id stream — this is the size-flush trigger boundary.
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
    /// Verifies that both BulkTrackIdCount and BulkAlbumIdCount are zero when all streams are empty.
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
    /// Verifies that GetOldestEnqueuedAtAsync returns null when the stream is empty.
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
    /// Verifies that GetOldestEnqueuedAtAsync parses the enqueuedAt field from the oldest entry
    /// in the track-id stream.
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
    /// Verifies that GetOldestEnqueuedAtAsync checks the album-id stream when isTracks=false.
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
    /// Verifies that GetOldestEnqueuedAtAsync returns null when the enqueuedAt field is absent.
    /// Ensures the helper degrades gracefully for messages without the field.
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
    /// Verifies that <see cref="SpotifyBulkProcessorService.ShouldFlush"/> returns true
    /// when count equals the size threshold (immediate size flush).
    /// Failure-first: the predicate was extracted in M7a; before extraction it was inlined
    /// in ShouldProcessBulkTracksAsync and the count check could not be unit-tested in isolation.
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
    /// Verifies that count below threshold with an old-enough message triggers the age flush.
    /// This is the low-volume correctness guarantee: a single stale message must not wait forever.
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
    /// Verifies that count below threshold with a fresh message does NOT trigger the age flush.
    /// Ensures premature flushes on just-enqueued messages are prevented.
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
    /// Verifies that count == 0 never triggers a flush even if oldestAge is large.
    /// An empty stream must not cause processing.
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
    /// Verifies that count below threshold with null oldestAge does NOT flush.
    /// Null oldestAge occurs when the enqueuedAt field is absent or the stream is empty —
    /// the linger check cannot fire without a timestamp.
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
    /// At <see cref="SpotifyBatchSettings.DefaultLingerMs"/> (24 h) scale:
    /// a single message whose age equals DefaultLingerMs triggers ShouldFlush,
    /// while a message just under that age does not.
    /// Uses the constant directly so the test is not sensitive to the literal value.
    /// Failure-first: before ShouldFlush was extracted into a pure function, the linger
    /// threshold was inlined and could not be exercised here with arbitrary ages.
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

    private static StreamEntry CreateStreamEntryWithEnqueuedAt( DateTimeOffset enqueuedAt ) {
        NameValueEntry[] values = [
            new NameValueEntry( "payload", "{\"lookupType\":0}" ),
            new NameValueEntry( "enqueuedAt", enqueuedAt.ToString( "O" ) )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", values );
    }

    private static StreamEntry CreateStreamEntryWithoutEnqueuedAt( ) {
        NameValueEntry[] values = [
            new NameValueEntry( "payload", "{\"lookupType\":0}" )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", values );
    }

    #endregion
}
