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
/// Unit tests spanning the Spotify bulk path's flush and routing seams: the
/// <see cref="SpotifyBulkQueueDecorator"/> delegation of interactive-priority typed lookups to the
/// inner queue (with no warning emitted), the pure <c>SpotifyBulkProcessorService.ShouldFlush</c>
/// predicate at the size threshold and the inclusive 24-hour-backstop age boundary, the
/// oldest-enqueued-at read's malformed-versus-well-formed handling, and the ordering guarantee that
/// rate-limit and retry-cap handling call <c>GetOrCreateAsync</c> to materialize the saga core hash
/// before writing provider state. Also asserts that interactive Spotify id lookups are never treated
/// as approved bulk callers.
/// </summary>
[TestClass]
public class SpotifyBulkFlushTests {

    /// <summary>Mock Redis multiplexer supplying the database and subscriber to the service under test.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock Redis database backing all stream operations.</summary>
    private Mock<IDatabase> _dbMock = null!;
    /// <summary>Mock Redis subscriber used to assert completion and sentinel publishes.</summary>
    private Mock<ISubscriber> _subscriberMock = null!;
    /// <summary>Mock rate-limit tracker the service consults and updates on rate-limit handling.</summary>
    private Mock<IRateLimitTracker> _rateLimitTrackerMock = null!;
    /// <summary>Mock saga state manager used to assert saga create/update ordering.</summary>
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    /// <summary>Mock bulk lookup service supplying batch track/album results.</summary>
    private Mock<ISpotifyBulkLookupService> _lookupServiceMock = null!;
    /// <summary>Mock logger for the batch queue helper.</summary>
    private Mock<ILogger<SpotifyBatchQueueHelper>> _helperLoggerMock = null!;
    /// <summary>Mock logger for the bulk processor service.</summary>
    private Mock<ILogger<SpotifyBulkProcessorService>> _serviceLoggerMock = null!;
    /// <summary>Mock inner queue the decorator wraps.</summary>
    private Mock<IRequestQueue<QueuedLookupRequest>> _innerQueueMock = null!;
    /// <summary>Mock logger for the decorator, used to assert warning suppression.</summary>
    private Mock<ILogger<SpotifyBulkQueueDecorator>> _decoratorLoggerMock = null!;

    /// <summary>Redis stream key for bulk Spotify track-id lookups.</summary>
    private const string TrackStream = SpotifyConstants.BulkTrackIdStream;
    /// <summary>Redis stream key for bulk Spotify album-id lookups.</summary>
    private const string AlbumStream = SpotifyConstants.BulkAlbumIdStream;
    /// <summary>Sample Spotify track id used throughout the tests.</summary>
    private const string TestTrackId = "3n3Ppam7vgaVa1iaRUc9Lp";
    /// <summary>Fixed saga id the mocks return for the sample track.</summary>
    private const string TestSagaId = "aabbccddeeff00112233445566778899";

    /// <summary>Serialization options (camel-case, non-indented) used to build stream payloads.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>MSTest-injected context; its cancellation token bounds the async operations under test.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates fresh mocks before each test and wires their default behaviors: Redis database and
    /// subscriber resolution, no-op stream operations, a not-rate-limited tracker state, and saga
    /// manager methods that return a saga for the sample track and complete successfully.
    /// </summary>
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

    /// <summary>
    /// Verifies that a song-id lookup at interactive priority is delegated to the inner queue and is
    /// never written to a bulk stream.
    /// </summary>
    /// <remarks>
    /// This passthrough is load-bearing for the 4xx bulk-rejection fallback. When
    /// <see cref="SpotifyBulkProcessorService"/> handles a <c>SpotifyBulkRejectedException</c>, it
    /// re-enqueues each item at Interactive priority through the decorator. If the decorator were ever
    /// changed to batch Interactive SongIdLookup requests into the bulk stream, the fallback would
    /// silently loop instead of resolving items individually.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_SongIdLookup_WithInteractivePriority_DelegatesToInnerQueue( ) {
        // Arrange — SongIdLookup with explicitly Interactive priority
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — inner queue receives the call (interactive lane, not bulk stream)
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "SongIdLookup at Interactive priority must delegate to the inner queue, not the bulk stream" );

        // Assert — no XADD on any bulk stream
        _dbMock.Verify(
            d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "Interactive SongIdLookup must not be written to any bulk stream" );
    }

    /// <summary>
    /// Verifies that an album-id lookup at interactive priority is delegated to the inner queue and
    /// is never written to a bulk stream.
    /// </summary>
    /// <remarks>
    /// This passthrough is load-bearing for the 4xx bulk-rejection fallback. When
    /// <see cref="SpotifyBulkProcessorService"/> handles a <c>SpotifyBulkRejectedException</c>, it
    /// re-enqueues each item at Interactive priority through the decorator. If the decorator were ever
    /// changed to batch Interactive AlbumIdLookup requests into the bulk stream, the fallback would
    /// silently loop instead of resolving items individually.
    /// </remarks>
    [TestMethod]
    public async Task Decorator_AlbumIdLookup_WithInteractivePriority_DelegatesToInnerQueue( ) {
        // Arrange
        QueuedLookupRequest request = MakeAlbumRequest( "6WdSsBrH5QtofaTTqgwxOV", QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — inner queue receives the call (interactive lane)
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "AlbumIdLookup at Interactive priority must delegate to the inner queue, not the bulk stream" );

        // Assert — no XADD on any bulk stream
        _dbMock.Verify(
            d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "Interactive AlbumIdLookup must not be written to any bulk stream" );
    }

    /// <summary>
    /// Verifies that enqueuing a bulk song-id lookup does not materialize a saga: sagas are created
    /// later, at flush time in result processing, not at enqueue time.
    /// </summary>
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
            "Decorator must not materialize a saga at enqueue time — sagas are created at flush in ProcessBulkResultAsync" );
    }

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
    /// Verifies the degradation contract for a malformed <c>enqueuedAt</c> surfaced as a null age:
    /// the age trigger does not fire, but the size trigger still does when the count reaches the
    /// threshold.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_MalformedEnqueuedAt_NullAge_DoesNotFlushOnAge_ButFlushesOnSize( ) {
        TimeSpan backstop = TimeSpan.FromMilliseconds( SpotifyBatchSettings.DefaultLingerMs );

        // Sub-threshold count + null age → no flush (age trigger cannot fire)
        bool ageResult = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: backstop );
        Assert.IsFalse( ageResult, "Null oldestAge must not trigger an age flush (safe degradation for malformed enqueuedAt)" );

        // At-threshold count + null age → flush (size trigger fires)
        bool sizeResult = SpotifyBulkProcessorService.ShouldFlush(
            count: SpotifyConstants.MaxTracksPerBatchLookup,
            oldestAge: null,
            threshold: SpotifyConstants.MaxTracksPerBatchLookup,
            linger: backstop );
        Assert.IsTrue( sizeResult, "Size trigger must still fire when oldestAge is null" );
    }

    /// <summary>
    /// Verifies that a malformed <c>enqueuedAt</c> value causes <c>GetOldestEnqueuedAtAsync</c> to
    /// return null and log a warning, so an unparseable timestamp degrades safely rather than
    /// throwing.
    /// </summary>
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
        Assert.IsNull( result, "Malformed enqueuedAt must return null (safe degradation)" );

        // Assert (b): a warning-level log was emitted
        _helperLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.AtLeastOnce,
            "A warning log must be emitted for a malformed enqueuedAt field" );
    }

    /// <summary>
    /// Verifies that a well-formed round-trip <c>O</c>-format <c>enqueuedAt</c> value is parsed back
    /// to the original timestamp by <c>GetOldestEnqueuedAtAsync</c>.
    /// </summary>
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

    /// <summary>
    /// Verifies the ordering guarantee in rate-limit handling: <c>HandleBulkRateLimitAsync</c> calls
    /// <c>GetOrCreateAsync</c> to ensure the saga core hash exists before <c>SetIsPartialAsync</c>
    /// writes provider state, so partial state is never written against a missing saga.
    /// </summary>
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
            "HandleBulkRateLimitAsync must call GetOrCreateAsync to ensure the core hash exists before writing provider state" );

        // Assert (b): GetOrCreateAsync was called BEFORE SetIsPartialAsync
        Assert.IsTrue( getOrCreateCalledBeforeSetPartial,
            "GetOrCreateAsync must be called before SetIsPartialAsync so the core hash exists before provider state is written" );
    }

    /// <summary>
    /// Verifies the same ordering guarantee on the retry-cap path: when a message reaches the retry
    /// cap, the requeue-single cap branch calls <c>GetOrCreateAsync</c> before
    /// <c>UpdateProviderStateAsync</c>, so the failed provider state is written against an existing
    /// saga.
    /// </summary>
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
            "RequeueSingleAsync CapReached branch must call GetOrCreateAsync to ensure the core hash exists before writing provider state" );

        // Assert (b): GetOrCreateAsync preceded UpdateProviderStateAsync
        Assert.IsTrue( getOrCreateCalledBeforeUpdateProvider,
            "GetOrCreateAsync must be called before UpdateProviderStateAsync in the CapReached branch" );
    }

    /// <summary>
    /// Verifies that Spotify is never an approved interactive caller for provider-id lookups:
    /// because Spotify song-id and album-id lookups route to the 24-hour-linger bulk stream
    /// regardless of priority, an interactive caller would not get a timely result. The test also
    /// fails if a newly added provider is neither approved nor explicitly banned, forcing
    /// classification before merge.
    /// </summary>
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

    /// <summary>
    /// Verifies that a song-id lookup at interactive priority does not emit a warning log: the item
    /// is correctly passed through to the inner queue, so the warning guard (which fires only on a
    /// genuine misroute) stays silent.
    /// </summary>
    [TestMethod]
    public async Task Decorator_SongIdLookup_WithInteractivePriority_DoesNotEmitWarning( ) {
        // Arrange
        QueuedLookupRequest request = MakeTrackRequest( TestTrackId, QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        _ = _decoratorLoggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — no WARNING (the routing-fix removed the interactive-warning branch)
        _decoratorLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Never,
            "Decorator must not emit a WARNING for SongIdLookup at Interactive priority — the item is now correctly passed through to the inner queue" );

        // Assert — inner queue received the call
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "Inner queue must be called for SongIdLookup at Interactive priority" );
    }

    /// <summary>
    /// Verifies that an album-id lookup at interactive priority likewise emits no warning and is
    /// passed through to the inner queue.
    /// </summary>
    [TestMethod]
    public async Task Decorator_AlbumIdLookup_WithInteractivePriority_DoesNotEmitWarning( ) {
        // Arrange
        QueuedLookupRequest request = MakeAlbumRequest( "6WdSsBrH5QtofaTTqgwxOV", QueuePriority.Interactive );
        SpotifyBulkQueueDecorator decorator = CreateDecorator( );

        _ = _decoratorLoggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );
        _ = _innerQueueMock
            .Setup( q => q.EnqueueAsync( It.IsAny<QueuedLookupRequest>( ), It.IsAny<QueuePriority>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        // Act
        await decorator.EnqueueAsync( request, QueuePriority.Interactive, TestContext.CancellationToken );

        // Assert — no WARNING
        _decoratorLoggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception?>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ),
            Times.Never,
            "Decorator must not emit a WARNING for AlbumIdLookup at Interactive priority" );

        // Assert — inner queue received the call
        _innerQueueMock.Verify(
            q => q.EnqueueAsync( request, QueuePriority.Interactive, It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "Inner queue must be called for AlbumIdLookup at Interactive priority" );
    }

    /// <summary>
    /// Verifies that a song-id lookup at non-interactive priorities (background and bulk) emits no
    /// warning: these are correctly bulk-routed, and the warning guard fires only on interactive
    /// misroutes. The test clears the logger's recorded invocations between priorities.
    /// </summary>
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
                $"Decorator must NOT emit a WARNING log for {priority} priority SongIdLookup (warning guard fires only on Interactive; these are bulk-routed non-interactive requests)" );
        }
    }

    /// <summary>
    /// Builds a decorator wrapping the inner-queue mock and wired to Redis and the decorator logger.
    /// </summary>
    /// <returns>A decorator under test.</returns>
    private SpotifyBulkQueueDecorator CreateDecorator( ) =>
        new( _innerQueueMock.Object, _redisMock.Object, _decoratorLoggerMock.Object );

    /// <summary>
    /// Builds a bulk processor service wired to the mocks and a batch settings instance using the
    /// default linger.
    /// </summary>
    /// <returns>A service under test.</returns>
    private SpotifyBulkProcessorService CreateService( ) {
        IOptions<SpotifyBatchSettings> options = Options.Create(
            new SpotifyBatchSettings { LingerMs = SpotifyBatchSettings.DefaultLingerMs } );
        return new SpotifyBulkProcessorService(
            _redisMock.Object,
            CreateHelper( ),
            _rateLimitTrackerMock.Object,
            _sagaManagerMock.Object,
            _lookupServiceMock.Object,
            _innerQueueMock.Object,
            _serviceLoggerMock.Object,
            options );
    }

    /// <summary>
    /// Builds a batch queue helper wired to Redis and the helper logger.
    /// </summary>
    /// <returns>A helper under test.</returns>
    private SpotifyBatchQueueHelper CreateHelper( ) =>
        new( _redisMock.Object, _helperLoggerMock.Object );

    /// <summary>
    /// Builds a Spotify song-id <c>QueuedLookupRequest</c> for the given track id, priority, and
    /// attempt count, carrying the fixed test saga id.
    /// </summary>
    /// <param name="trackId">The track id to look up.</param>
    /// <param name="priority">The origin priority; defaults to bulk.</param>
    /// <param name="attemptCount">The retry attempt count; defaults to zero.</param>
    /// <returns>A track lookup request.</returns>
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

    /// <summary>
    /// Builds a Spotify album-id <c>QueuedLookupRequest</c> for the given album id, priority, and
    /// attempt count, carrying the fixed test saga id.
    /// </summary>
    /// <param name="albumId">The album id to look up.</param>
    /// <param name="priority">The origin priority; defaults to bulk.</param>
    /// <param name="attemptCount">The retry attempt count; defaults to zero.</param>
    /// <returns>An album lookup request.</returns>
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

    /// <summary>
    /// Builds a Redis stream entry whose <c>payload</c> field is the serialized request and whose
    /// <c>enqueuedAt</c> field is the current time, for tests that feed entries through the helper's
    /// dequeue and requeue paths.
    /// </summary>
    /// <param name="request">The request to embed as the entry payload.</param>
    /// <returns>A stream entry carrying the serialized request and an enqueue timestamp.</returns>
    private static StreamEntry BuildStreamEntry( QueuedLookupRequest request ) {
        string payload = System.Text.Json.JsonSerializer.Serialize( request, s_jsonOptions );
        NameValueEntry[] fields = [
            new NameValueEntry( "payload", payload ),
            new NameValueEntry( "enqueuedAt", DateTimeOffset.UtcNow.ToString( "O" ) )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", fields );
    }
}
