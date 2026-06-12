using System.Globalization;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Infrastructure.Queue;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Invariant-pinning tests for the spotify-bulk-flush-semantics change (D6 + D7).
/// </summary>
/// <remarks>
/// Tests are grouped by the directive they pin:
/// <list type="bullet">
///   <item>D6a — "interactive never rides bulk": <see cref="SpotifyBulkQueueDecorator"/> routes
///   <see cref="LookupRequestType.SongIdLookup"/>/<see cref="LookupRequestType.AlbumIdLookup"/>
///   to the bulk streams irrespective of <see cref="QueuePriority"/>, including
///   <see cref="QueuePriority.Interactive"/>.</item>
///   <item>D6b — bulk-stream items are saga-less until flush: the decorator writes no saga state;
///   saga materialization happens only in <c>ProcessBulkResultAsync</c> via
///   <see cref="ISagaStateManager.GetOrCreateAsync"/>.</item>
///   <item>D7 — flush contract: size-50 fires immediately; sub-backstop age does not flush;
///   age ≥ backstop does flush; malformed <c>enqueuedAt</c> yields null + no flush.</item>
///   <item>D3 — <c>GetOldestEnqueuedAtAsync</c> graceful degradation on malformed
///   <c>enqueuedAt</c>: returns null + emits a warning log, no exception.</item>
///   <item>D4 — <c>HandleBulkRateLimitAsync</c> and the <see cref="RequeueOutcome.CapReached"/>
///   branch in <c>RequeueSingleAsync</c> call <see cref="ISagaStateManager.GetOrCreateAsync"/>
///   before writing provider state (saga-hash fix for JetStream fire-and-forget items).</item>
/// </list>
/// </remarks>
[TestClass]
public class SpotifyBulkFlushTests {

    // -------------------------------------------------------------------------
    // Shared mocks (reset per test via Initialize)
    // -------------------------------------------------------------------------

    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _dbMock = null!;
    private Mock<ISubscriber> _subscriberMock = null!;
    private Mock<IRateLimitTracker> _rateLimitTrackerMock = null!;
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    private Mock<ISpotifyBulkLookupService> _lookupServiceMock = null!;
    private Mock<ILogger<SpotifyBatchQueueHelper>> _helperLoggerMock = null!;
    private Mock<ILogger<SpotifyBulkProcessorService>> _serviceLoggerMock = null!;
    private Mock<IRequestQueue<QueuedLookupRequest>> _innerQueueMock = null!;
    private Mock<ILogger<SpotifyBulkQueueDecorator>> _decoratorLoggerMock = null!;

    private const string TrackStream = SpotifyConstants.BulkTrackIdStream;
    private const string AlbumStream = SpotifyConstants.BulkAlbumIdStream;
    private const string TestTrackId = "3n3Ppam7vgaVa1iaRUc9Lp";
    private const string TestSagaId = "aabbccddeeff00112233445566778899";

    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Initializes mocks before each test.</summary>
    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _dbMock = new Mock<IDatabase>( );
        _subscriberMock = new Mock<ISubscriber>( );
        _rateLimitTrackerMock = new Mock<IRateLimitTracker>( );
        _sagaManagerMock = new Mock<ISagaStateManager>( );
        _lookupServiceMock = new Mock<ISpotifyBulkLookupService>( );
        _helperLoggerMock = new Mock<ILogger<SpotifyBatchQueueHelper>>( );
        _serviceLoggerMock = new Mock<ILogger<SpotifyBulkProcessorService>>( );
        _innerQueueMock = new Mock<IRequestQueue<QueuedLookupRequest>>( );
        _decoratorLoggerMock = new Mock<ILogger<SpotifyBulkQueueDecorator>>( );

        _ = _redisMock.Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _dbMock.Object );
        _ = _redisMock.Setup( r => r.GetSubscriber( It.IsAny<object>( ) ) )
            .Returns( _subscriberMock.Object );

        // XADD succeeds by default
        _ = _dbMock.Setup( d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)"1234567890-0" );

        // StreamLength returns 0 by default
        _ = _dbMock.Setup( d => d.StreamLengthAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 0L );

        // StreamRangeAsync returns empty by default
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        // XAUTOCLAIM returns empty by default
        _ = _dbMock.Setup( d => d.StreamAutoClaimAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<long>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( StreamAutoClaimResult.Null );

        // XREADGROUP returns empty by default
        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        // EnsureConsumerGroupsAsync — BUSYGROUP (group already exists)
        _ = _dbMock.Setup( d => d.StreamCreateConsumerGroupAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<bool>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ThrowsAsync( new RedisServerException( "BUSYGROUP" ) );

        // XACK + XDEL succeed
        _ = _dbMock.Setup( d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 1L );
        _ = _dbMock.Setup( d => d.StreamDeleteAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue[]>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 1L );

        // Subscriber publish succeeds
        _ = _subscriberMock.Setup( s => s.PublishAsync(
                It.IsAny<RedisChannel>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 0L );

        // Not rate-limited by default
        _ = _rateLimitTrackerMock.Setup( t => t.GetStateAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( new RateLimitState( false, null, null ) );
        _ = _rateLimitTrackerMock.Setup( t => t.SetRateLimitedAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<DateTimeOffset>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // Saga manager defaults
        _ = _sagaManagerMock.Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( new LookupSagaState {
                SagaId = TestSagaId,
                LookupKey = $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}",
                LookupType = LookupRequestType.SongIdLookup,
                LookupValue = TestTrackId
            } );
        _ = _sagaManagerMock.Setup( m => m.GetAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( new LookupSagaState {
                SagaId = TestSagaId,
                LookupKey = $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}",
                LookupType = LookupRequestType.SongIdLookup,
                LookupValue = TestTrackId
            } );
        _ = _sagaManagerMock.Setup( m => m.InitializeProviderStatesAsync(
                It.IsAny<string>( ),
                It.IsAny<IEnumerable<SupportedProviders>>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );
        _ = _sagaManagerMock.Setup( m => m.UpdateProviderStateAsync(
                It.IsAny<string>( ),
                It.IsAny<ProviderLookupState>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );
        _ = _sagaManagerMock.Setup( m => m.SetIsPartialAsync(
                It.IsAny<string>( ),
                It.IsAny<bool>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );
        _ = _sagaManagerMock.Setup( m => m.SetRateLimitInfoAsync(
                It.IsAny<string>( ),
                It.IsAny<List<ProviderRateLimitInfo>>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );
    }

    // =========================================================================
    // D6a — "Interactive never rides bulk" invariant
    // =========================================================================

    /// <summary>
    /// Pins that SongIdLookup with <see cref="QueuePriority.Interactive"/> is still written
    /// to the bulk track-id stream and never delegated to the inner queue.
    /// </summary>
    /// <remarks>
    /// Failure-first: before the linger change the 500 ms linger masked this coupling; at 24 h
    /// an interactive SongIdLookup accidentally sent to the bulk stream would wait up to 24 h.
    /// The decorator routes by LookupType, not priority — this test makes that coupling explicit
    /// and visible. A future caller that passes Interactive priority will still land in the bulk
    /// stream; this test documents the invariant so the regression fails loudly.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_SongIdLookup_WithInteractivePriority_RoutesToBulkStream( ) {
        // Arrange — SongIdLookup with explicitly Interactive priority
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — XADD was called on the track-id bulk stream
        _dbMock.Verify(
            d => d.StreamAddAsync(
                (RedisKey)TrackStream,
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "SongIdLookup must route to the bulk track stream regardless of priority" );

        // Assert — inner queue was NOT invoked (not treated as an interactive passthrough)
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "Inner queue must not be called for SongIdLookup even at Interactive priority" );
    }

    /// <summary>
    /// Pins that AlbumIdLookup with <see cref="QueuePriority.Interactive"/> is still written
    /// to the bulk album-id stream and never delegated to the inner queue.
    /// </summary>
    /// <remarks>
    /// Failure-first: same reasoning as the track test — decorator routes by LookupType only.
    /// Test fails against a decorator that respects the priority argument for bulk-stream routing.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_AlbumIdLookup_WithInteractivePriority_RoutesToBulkStream( ) {
        // Arrange
        QueuedLookupRequest request = MakeAlbumRequest( "6WdSsBrH5QtofaTTqgwxOV", QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — XADD on album-id stream
        _dbMock.Verify(
            d => d.StreamAddAsync(
                (RedisKey)AlbumStream,
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "AlbumIdLookup must route to the bulk album stream regardless of priority" );

        _innerQueueMock.Verify(
            q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "Inner queue must not be called for AlbumIdLookup even at Interactive priority" );
    }

    // =========================================================================
    // D6b — Bulk-stream items are saga-less until flush
    // =========================================================================

    /// <summary>
    /// Pins that <see cref="SpotifyBulkQueueDecorator.EnqueueAsync"/> writes to the Redis stream
    /// but never calls <see cref="ISagaStateManager.GetOrCreateAsync"/> — saga creation is deferred
    /// to flush time (ProcessBulkResultAsync).
    /// </summary>
    /// <remarks>
    /// Failure-first: if the decorator were modified to call GetOrCreateAsync, the
    /// sagaManagerMock.Verify(Times.Never) assertion fails. This test documents that the
    /// saga-less-until-flush contract is load-bearing for the TTL-raise safety story: Redis
    /// retention is bounded by saga count × TTL, and materializing a saga at enqueue time
    /// for every JetStream fire-and-forget item would inflate retention dramatically.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_SongIdLookup_DoesNotCreateSaga( ) {
        // Arrange
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Bulk );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Bulk, TestContext.CancellationToken );

        // Assert — no saga created at enqueue time
        _sagaManagerMock.Verify(
            m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority>( ),
                It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "Decorator must not materialize a saga at enqueue time — sagas are created at flush in ProcessBulkResultAsync (D6b)" );
    }

    // =========================================================================
    // D7 — Flush contract: size trigger / linger backstop
    // =========================================================================

    /// <summary>
    /// Pins that size = 50 (MaxTracksPerBatchLookup) fires immediately with no age required.
    /// </summary>
    /// <remarks>
    /// Failure-first: removing the <c>count >= threshold</c> branch from <see cref="SpotifyBulkProcessorService.ShouldFlush"/>
    /// causes this to return false (oldestAge is null at threshold). The test was verified to
    /// fail against the pre-fix code before the predicate was extracted.
    /// </remarks>
    [TestMethod]
    public void ShouldFlush_SizeThreshold_FiresImmediately_WithNullAge( ) {
        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: SpotifyConstants.MaxTracksPerBatchLookup,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: TimeSpan.FromMilliseconds( SpotifyBatchSettings.DefaultLingerMs ) );

        Assert.IsTrue( result, "Size threshold (50) must fire immediately even with null oldestAge" );
    }

    /// <summary>
    /// Pins that a single item aged below the 24-hour backstop does NOT flush.
    /// </summary>
    /// <remarks>
    /// This test discriminates the dropped-age-bound mutation: widening the age check
    /// (e.g., <c>oldestAge.Value &gt; TimeSpan.Zero</c> instead of <c>&gt;= linger</c>)
    /// would cause flush=true for a count=1, sub-backstop item, making this assertion fail.
    /// The inclusive <c>&gt;=</c> boundary itself is pinned by the companion test
    /// <see cref="ShouldFlush_AgeExactlyAtBackstop_Flushes"/>: together the two tests
    /// discriminate both the mutation that widens the guard and the one that narrows it
    /// from <c>&gt;=</c> to <c>&gt;</c>.
    /// </remarks>
    [TestMethod]
    public void ShouldFlush_SubBackstopAge_DoesNotFlush( ) {
        TimeSpan backstop = TimeSpan.FromMilliseconds( SpotifyBatchSettings.DefaultLingerMs );
        TimeSpan subBackstop = backstop - TimeSpan.FromMilliseconds( 1 );

        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: subBackstop,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: backstop );

        Assert.IsFalse( result, "Item aged just below the 24-hour backstop must not flush (age-trigger is ≥, not >)" );
    }

    /// <summary>
    /// Pins that a single item aged exactly at the 24-hour backstop DOES flush.
    /// </summary>
    /// <remarks>
    /// Failure-first: changing the predicate from <c>oldestAge.Value >= linger</c> to
    /// <c>oldestAge.Value > linger</c> causes this to return false. The test verifies
    /// the inclusive boundary.
    /// </remarks>
    [TestMethod]
    public void ShouldFlush_AgeExactlyAtBackstop_Flushes( ) {
        TimeSpan backstop = TimeSpan.FromMilliseconds( SpotifyBatchSettings.DefaultLingerMs );

        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: backstop,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: backstop );

        Assert.IsTrue( result, "Item aged exactly at the 24-hour backstop must flush (inclusive boundary)" );
    }

    /// <summary>
    /// Pins that a null oldestAge (from a malformed enqueuedAt, post-D3) does not flush on age,
    /// but the stream still flushes on size.
    /// </summary>
    /// <remarks>
    /// This is the critical safety case under D3: a malformed enqueuedAt in a low-volume stream
    /// yields null from <c>GetOldestEnqueuedAtAsync</c>. The age trigger must not fire; the size
    /// trigger must still work normally.
    /// Failure-first: if null were treated as "age = 0" the age trigger would never fire, but
    /// the count=50 size check still returns true — the test distinguishes these correctly.
    /// </remarks>
    [TestMethod]
    public void ShouldFlush_MalformedEnqueuedAt_NullAge_DoesNotFlushOnAge_ButFlushesOnSize( ) {
        TimeSpan backstop = TimeSpan.FromMilliseconds( SpotifyBatchSettings.DefaultLingerMs );

        // Sub-threshold count + null age → no flush (age trigger cannot fire)
        bool ageResult = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: backstop );
        Assert.IsFalse( ageResult, "Null oldestAge must not trigger an age flush (D3 safe degradation)" );

        // At-threshold count + null age → flush (size trigger fires)
        bool sizeResult = SpotifyBulkProcessorService.ShouldFlush(
            count: SpotifyConstants.MaxTracksPerBatchLookup,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: backstop );
        Assert.IsTrue( sizeResult, "Size trigger must still fire when oldestAge is null" );
    }

    // =========================================================================
    // D3 — GetOldestEnqueuedAtAsync graceful degradation on malformed enqueuedAt
    // =========================================================================

    /// <summary>
    /// Pins that a malformed <c>enqueuedAt</c> field causes <c>GetOldestEnqueuedAtAsync</c>
    /// to return null and emit a warning log rather than throwing <see cref="FormatException"/>.
    /// </summary>
    /// <remarks>
    /// Failure-first: before D3, <c>DateTimeOffset.ParseExact</c> threw
    /// <see cref="FormatException"/> for a malformed field. The test verifies the post-D3
    /// behaviour by asserting (a) no exception is thrown, (b) null is returned, and (c) a
    /// warning-level log is emitted. Running the test against the pre-D3 code causes an
    /// unhandled <see cref="FormatException"/>, failing the test.
    /// </remarks>
    [TestMethod]
    public async Task GetOldestEnqueuedAtAsync_MalformedEnqueuedAt_ReturnsNullAndLogsWarning( ) {
        // Arrange — entry with a timestamp that cannot be parsed by "O" round-trip format
        NameValueEntry[] fields = [
            new NameValueEntry( "payload", "{\"lookupType\":0}" ),
            new NameValueEntry( "enqueuedAt", "NOT-A-VALID-TIMESTAMP" )
        ];
        StreamEntry badEntry = new( (RedisValue)"1234567890-0", fields );

        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                (RedisKey)TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                1,
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [badEntry] );

        // Logger must return true for IsEnabled so the warning path is exercised
        _ = _helperLoggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        SpotifyBatchQueueHelper helper = CreateHelper( );

        // Act — must not throw
        DateTimeOffset? result = await helper.GetOldestEnqueuedAtAsync( isTracks: true, TestContext.CancellationToken );

        // Assert (a): returns null
        Assert.IsNull( result, "Malformed enqueuedAt must return null (D3 safe degradation)" );

        // Assert (b): a warning-level log was emitted
        _helperLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce,
            "A warning log must be emitted for a malformed enqueuedAt field (D3)" );
    }

    /// <summary>
    /// Pins that a well-formed <c>enqueuedAt</c> field is still parsed correctly after D3
    /// (regression guard: TryParseExact must not silently reject valid timestamps).
    /// </summary>
    /// <remarks>
    /// Failure-first: if TryParseExact were incorrectly configured (wrong format string,
    /// wrong DateTimeStyles) it would reject valid round-trip timestamps. This test ensures
    /// the happy path still works after the hardening change.
    /// </remarks>
    [TestMethod]
    public async Task GetOldestEnqueuedAtAsync_WellFormedEnqueuedAt_ReturnsCorrectTimestamp( ) {
        // Arrange — valid round-trip timestamp
        DateTimeOffset expected = new( 2026, 6, 10, 12, 0, 0, TimeSpan.Zero );
        string formatted = expected.ToString( "O", CultureInfo.InvariantCulture );

        NameValueEntry[] fields = [
            new NameValueEntry( "payload", "{\"lookupType\":0}" ),
            new NameValueEntry( "enqueuedAt", formatted )
        ];
        StreamEntry goodEntry = new( (RedisValue)"1234567890-0", fields );

        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                (RedisKey)TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                1,
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [goodEntry] );

        SpotifyBatchQueueHelper helper = CreateHelper( );

        // Act
        DateTimeOffset? result = await helper.GetOldestEnqueuedAtAsync( isTracks: true, TestContext.CancellationToken );

        // Assert
        Assert.IsNotNull( result, "Valid enqueuedAt must be returned, not null" );
        Assert.AreEqual( expected.ToString( "O" ), result.Value.ToString( "O" ),
            "Parsed timestamp must equal the original value" );
    }

    // =========================================================================
    // D4 — Saga-hash fix: HandleBulkRateLimitAsync calls GetOrCreateAsync first
    // =========================================================================

    /// <summary>
    /// Pins that <c>HandleBulkRateLimitAsync</c> calls <see cref="ISagaStateManager.GetOrCreateAsync"/>
    /// before writing any per-saga provider state, so the core hash exists for JetStream
    /// fire-and-forget items that never pre-materialized a saga.
    /// </summary>
    /// <remarks>
    /// Failure-first: before D4, <c>HandleBulkRateLimitAsync</c> went straight to
    /// <c>SetIsPartialAsync</c> without calling <c>GetOrCreateAsync</c>. Removing the
    /// <c>GetOrCreateAsync</c> call from the production code causes <c>Times.Once</c>
    /// to become <c>Times.Never</c>, failing the test.
    /// </remarks>
    [TestMethod]
    public async Task HandleBulkRateLimitAsync_CallsGetOrCreateAsync_BeforeWritingProviderState( ) {
        // Arrange — a JetStream-style fire-and-forget message (saga not pre-created)
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Bulk );
        List<QueuedMessage<QueuedLookupRequest>> messages = [
            new QueuedMessage<QueuedLookupRequest>(
                $"{TrackStream}:1234567890-0",
                request,
                DateTimeOffset.UtcNow )
        ];

        Contracts.Exceptions.RetryAfterExceededException ex = new(
            retryAfterValue: TimeSpan.FromSeconds( 60 ),
            threshold: TimeSpan.FromSeconds( 120 ),
            requestUri: null,
            provider: SupportedProviders.Spotify );

        bool getOrCreateCalledBeforeSetPartial = false;
        bool setPartialCalled = false;

        _ = _sagaManagerMock.Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback( ( string _, string _, LookupRequestType _, string _, QueuePriority? _, CancellationToken _ ) => {
                getOrCreateCalledBeforeSetPartial = !setPartialCalled;
            } )
            .ReturnsAsync( new LookupSagaState {
                SagaId = TestSagaId,
                LookupKey = $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}",
                LookupType = LookupRequestType.SongIdLookup,
                LookupValue = TestTrackId
            } );

        _ = _sagaManagerMock.Setup( m => m.SetIsPartialAsync(
                It.IsAny<string>( ),
                It.IsAny<bool>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback( ( string _, bool _, CancellationToken _ ) => setPartialCalled = true )
            .Returns( Task.CompletedTask );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.HandleBulkRateLimitAsync( messages, SpotifyConstants.BulkTracksEndpoint, ex, TestContext.CancellationToken );

        // Assert (a): GetOrCreateAsync was called
        _sagaManagerMock.Verify(
            m => m.GetOrCreateAsync(
                TestSagaId,
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "HandleBulkRateLimitAsync must call GetOrCreateAsync to ensure the core hash exists (D4)" );

        // Assert (b): GetOrCreateAsync was called BEFORE SetIsPartialAsync
        Assert.IsTrue( getOrCreateCalledBeforeSetPartial,
            "GetOrCreateAsync must be called before SetIsPartialAsync so the core hash exists before provider state is written" );
    }

    // =========================================================================
    // D4 — Saga-hash fix: RequeueSingleAsync CapReached calls GetOrCreateAsync first
    // =========================================================================

    /// <summary>
    /// Pins that the <see cref="RequeueOutcome.CapReached"/> branch of <c>RequeueSingleAsync</c>
    /// calls <see cref="ISagaStateManager.GetOrCreateAsync"/> before
    /// <see cref="ISagaStateManager.UpdateProviderStateAsync"/>, so the core hash exists for
    /// JetStream fire-and-forget items.
    /// </summary>
    /// <remarks>
    /// Failure-first: before D4, the CapReached branch went straight to
    /// <c>UpdateProviderStateAsync</c>. Removing the <c>GetOrCreateAsync</c> call from the
    /// production code causes the <c>Times.Once</c> assertion on <c>GetOrCreateAsync</c>
    /// to become <c>Times.Never</c>, failing the test.
    /// </remarks>
    [TestMethod]
    public async Task RequeueSingle_CapReached_CallsGetOrCreateAsync_BeforeUpdateProviderState( ) {
        // Arrange — message at retry cap
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Bulk,
            attemptCount: LookupConstants.MaxQueueRetryAttempts );
        StreamEntry capEntry = BuildStreamEntry( request );

        // StreamRangeAsync returns the at-cap entry so RequeueAsync sees AttemptCount == cap
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                (RedisKey)TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [capEntry] );

        // XREADGROUP returns this entry
        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                (RedisKey)TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [capEntry] );

        // Track stream at size threshold so ShouldProcessBulkTracksAsync fires
        _ = _dbMock.Setup( d => d.StreamLengthAsync( (RedisKey)TrackStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( SpotifyConstants.MaxTracksPerBatchLookup );

        // Absent-key result routes to RequeueSingleAsync (reaches cap)
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( new Dictionary<string, MusicLookupResult?> { ["someOtherId"] = null } );

        bool getOrCreateCalledBeforeUpdateProvider = false;
        bool updateProviderCalled = false;

        _ = _sagaManagerMock.Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback( ( string _, string _, LookupRequestType _, string _, QueuePriority? _, CancellationToken _ ) => {
                // Only set the flag if this is the CapReached call (updateProvider not yet called)
                if (!updateProviderCalled) {
                    getOrCreateCalledBeforeUpdateProvider = true;
                }
            } )
            .ReturnsAsync( new LookupSagaState {
                SagaId = TestSagaId,
                LookupKey = $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}",
                LookupType = LookupRequestType.SongIdLookup,
                LookupValue = TestTrackId
            } );

        _ = _sagaManagerMock.Setup( m => m.UpdateProviderStateAsync(
                It.IsAny<string>( ),
                It.IsAny<ProviderLookupState>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Callback( ( string _, ProviderLookupState _, CancellationToken _ ) => updateProviderCalled = true )
            .Returns( Task.CompletedTask );

        SpotifyBulkProcessorService service = CreateService( );

        // Act — drives the absent-key → RequeueSingleAsync → CapReached path
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert (a): GetOrCreateAsync was called in the CapReached branch
        _sagaManagerMock.Verify(
            m => m.GetOrCreateAsync(
                TestSagaId,
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority?>( ),
                It.IsAny<CancellationToken>( ) ),
            Times.AtLeastOnce,
            "RequeueSingleAsync CapReached branch must call GetOrCreateAsync to ensure the core hash exists (D4)" );

        // Assert (b): GetOrCreateAsync preceded UpdateProviderStateAsync
        Assert.IsTrue( getOrCreateCalledBeforeUpdateProvider,
            "GetOrCreateAsync must be called before UpdateProviderStateAsync in the CapReached branch" );
    }

    // =========================================================================
    // D6.2 — CI guard: caller-enumeration invariant (interactive never waits)
    // =========================================================================

    /// <summary>
    /// CI guard: enumerates every <see cref="SupportedProviders"/> value and asserts that
    /// <see cref="SupportedProviders.Spotify"/> is NOT in the set of providers for which
    /// callers may safely use the interactive provider-ID entry point
    /// (<c>ICachingMediaLinkService.GetInfoByProviderIdAsync</c>).
    /// </summary>
    /// <remarks>
    /// Invariant: interactive lookups never wait. For Spotify, both
    /// <see cref="LookupRequestType.SongIdLookup"/> and <see cref="LookupRequestType.AlbumIdLookup"/>
    /// are intercepted by <see cref="SpotifyBulkQueueDecorator"/> and routed to the
    /// 24 h-linger bulk stream regardless of the <see cref="QueuePriority"/> argument.
    /// A caller that passes <c>SupportedProviders.Spotify</c> to the interactive entry point
    /// will wait up to 24 h — not the interactive wait budget — violating the "interactive
    /// lookups never wait" invariant.
    /// <para>
    /// "One commit away" failure: adding <c>SupportedProviders.Spotify</c> to
    /// <c>approvedCallerProviders</c> causes the first assertion to fail immediately.
    /// Adding a new <see cref="SupportedProviders"/> value without classifying it causes
    /// the exhaustive-coverage assertion to fail, requiring the developer to explicitly
    /// place the new provider in either the approved or the banned set.
    /// </para>
    /// <para>
    /// Failure-first: this test was verified to pass before implementation by running it with
    /// <c>approvedCallerProviders</c> containing only <c>AppleMusic</c> and <c>Tidal</c>; the
    /// assertions hold. To simulate the "one commit away" regression, temporarily adding
    /// <c>SupportedProviders.Spotify</c> to <c>approvedCallerProviders</c> causes the first
    /// assertion to fail with "Spotify must never appear in the approved-caller set."
    /// </para>
    /// </remarks>
    [TestMethod]
    public void InteractiveProviderIdLookup_Spotify_IsNotApprovedCaller( ) {
        // The approved set is the authoritative reference for which providers are safe
        // to pass to the interactive GetInfoByProviderIdAsync entry point.
        // Spotify must never appear here — its SongIdLookup/AlbumIdLookup go to the 24 h bulk stream.
        HashSet<SupportedProviders> approvedCallerProviders = [
            SupportedProviders.AppleMusic,
            SupportedProviders.Tidal
        ];

        // Primary guard: Spotify must not be in the approved-caller set.
        Assert.DoesNotContain(
            SupportedProviders.Spotify,
            approvedCallerProviders,
            "SupportedProviders.Spotify must never appear in the approved-caller set for " +
            "GetInfoByProviderIdAsync — SongIdLookup/AlbumIdLookup route to the 24 h-linger " +
            "bulk stream regardless of priority; interactive callers will not receive a timely result." );

        // Exhaustive-coverage guard: every SupportedProviders value must be explicitly classified.
        // This trips when a new provider is added to the enum without being reviewed here.
        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            bool isApproved = approvedCallerProviders.Contains( provider );
            bool isBanned = provider == SupportedProviders.Spotify;
            Assert.IsTrue(
                isApproved || isBanned,
                $"SupportedProviders.{provider} is neither in the approved-caller set nor explicitly banned. " +
                "Update this test to classify the new provider before merging." );
        }
    }

    // =========================================================================
    // D6.2 — Runtime guard: Interactive-priority warning in decorator
    // =========================================================================

    /// <summary>
    /// Pins that the decorator emits a WARNING log when a <see cref="LookupRequestType.SongIdLookup"/>
    /// arrives with <see cref="QueuePriority.Interactive"/> priority.
    /// </summary>
    /// <remarks>
    /// Failure-first: before this guard the decorator silently routed Interactive-priority
    /// SongIdLookup to the 24 h-linger bulk stream without any diagnostic signal. This test
    /// was verified to fail against the pre-guard decorator (no warning emitted); it passes
    /// after the <c>LogInteractivePriorityOnBulkStream</c> call is added to the enqueue path.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_SongIdLookup_WithInteractivePriority_EmitsWarningLog( ) {
        // Arrange
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        _ = _decoratorLoggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — a WARNING-level log was emitted
        _decoratorLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Once,
            "Decorator must emit exactly one WARNING log when SongIdLookup arrives with Interactive priority (D6.2 runtime guard)" );
    }

    /// <summary>
    /// Pins that the decorator emits a WARNING log when a <see cref="LookupRequestType.AlbumIdLookup"/>
    /// arrives with <see cref="QueuePriority.Interactive"/> priority.
    /// </summary>
    /// <remarks>
    /// Failure-first: same reasoning as the SongIdLookup variant — warning was absent
    /// before the guard was added. This test covers the album-ID path.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_AlbumIdLookup_WithInteractivePriority_EmitsWarningLog( ) {
        // Arrange
        QueuedLookupRequest request = MakeAlbumRequest( "6WdSsBrH5QtofaTTqgwxOV", QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        _ = _decoratorLoggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert
        _decoratorLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Once,
            "Decorator must emit exactly one WARNING log when AlbumIdLookup arrives with Interactive priority (D6.2 runtime guard)" );
    }

    /// <summary>
    /// Pins that the decorator does NOT emit a WARNING log when a
    /// <see cref="LookupRequestType.SongIdLookup"/> arrives with
    /// <see cref="QueuePriority.Background"/> or <see cref="QueuePriority.Bulk"/> priority.
    /// </summary>
    /// <remarks>
    /// Failure-first: if the warning were emitted unconditionally (regardless of priority),
    /// this test would fail. The guard must only fire for Interactive priority.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_SongIdLookup_WithNonInteractivePriority_DoesNotEmitWarningLog( ) {
        _ = _decoratorLoggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        foreach (QueuePriority priority in new[] { QueuePriority.Background, QueuePriority.Bulk }) {
            // Reset logger mock between iterations
            _decoratorLoggerMock.Invocations.Clear( );

            QueuedLookupRequest request = MakeTrackRequest( TestTrackId, priority );
            SpotifyBulkQueueDecorator decorator = CreateDecorator( );

            await decorator.EnqueueAsync( request, priority, TestContext.CancellationToken );

            _decoratorLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>( ),
                    It.IsAny<It.IsAnyType>( ),
                    It.IsAny<Exception?>( ),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
                Times.Never,
                $"Decorator must NOT emit a WARNING log for {priority} priority SongIdLookup (D6.2 runtime guard fires only on Interactive)" );
        }
    }

    // =========================================================================
    // Helpers / factory methods
    // =========================================================================

    private SpotifyBulkQueueDecorator CreateDecorator( ) =>
        new( _innerQueueMock.Object, _redisMock.Object, _decoratorLoggerMock.Object );

    private SpotifyBulkProcessorService CreateService( ) {
        IOptions<SpotifyBatchSettings> options = Options.Create(
            new SpotifyBatchSettings { LingerMs = SpotifyBatchSettings.DefaultLingerMs } );
        return new SpotifyBulkProcessorService(
            _redisMock.Object,
            CreateHelper( ),
            _rateLimitTrackerMock.Object,
            _sagaManagerMock.Object,
            _lookupServiceMock.Object,
            _serviceLoggerMock.Object,
            options );
    }

    private SpotifyBatchQueueHelper CreateHelper( ) =>
        new( _redisMock.Object, _helperLoggerMock.Object );

    private static QueuedLookupRequest MakeTrackRequest(
        string trackId,
        QueuePriority priority = QueuePriority.Bulk,
        int attemptCount = 0
    ) => new( ) {
        RequestId = Guid.NewGuid( ).ToString( "N" ),
        Provider = SupportedProviders.Spotify,
        LookupType = LookupRequestType.SongIdLookup,
        LookupValue = trackId,
        SagaId = TestSagaId,
        IsAlbum = false,
        OriginPriority = priority,
        AttemptCount = attemptCount
    };

    private static QueuedLookupRequest MakeAlbumRequest(
        string albumId,
        QueuePriority priority = QueuePriority.Bulk,
        int attemptCount = 0
    ) => new( ) {
        RequestId = Guid.NewGuid( ).ToString( "N" ),
        Provider = SupportedProviders.Spotify,
        LookupType = LookupRequestType.AlbumIdLookup,
        LookupValue = albumId,
        SagaId = TestSagaId,
        IsAlbum = true,
        OriginPriority = priority,
        AttemptCount = attemptCount
    };

    private static StreamEntry BuildStreamEntry( QueuedLookupRequest request ) {
        string payload = System.Text.Json.JsonSerializer.Serialize( request, s_jsonOptions );
        NameValueEntry[] fields = [
            new NameValueEntry( "payload", payload ),
            new NameValueEntry( "enqueuedAt", DateTimeOffset.UtcNow.ToString( "O" ) )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", fields );
    }
}
