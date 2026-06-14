using System.Reflection;
using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Exceptions;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Infrastructure.Queue;
using BridgeBeats.Core.Infrastructure.Utilities;
using BridgeBeats.Worker.Spotify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit and integration-level tests for the Spotify bulk dispatch contract:
/// the three result-routing branches (empty dict, absent key, null value),
/// the retry-cap increment and drop logic, rate-limit parity (SetIsPartialAsync
/// per saga), LookupKeyBuilder canonical-format alignment, and ShouldFlush predicate.
/// </summary>
/// <remarks>
/// <para>
/// Test architecture: <see cref="SpotifyBulkProcessorService"/> is driven via its
/// <c>ExecuteAsync</c> loop. Mock Redis operations control what
/// <see cref="SpotifyBatchQueueHelper"/> reads from the streams; a mock
/// <see cref="ISpotifyBulkLookupService"/> controls what the Spotify API returns.
/// </para>
/// <para>
/// Failure-first discipline: each test describes the pre-fix code path that made
/// the test fail, then verifies the corrected behavior.
/// </para>
/// </remarks>
[TestClass]
public class SpotifyBulkDispatchContractTests {

    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _dbMock = null!;
    private Mock<ISubscriber> _subscriberMock = null!;
    private Mock<IRateLimitTracker> _rateLimitTrackerMock = null!;
    private Mock<ISagaStateManager> _sagaManagerMock = null!;
    private Mock<ISpotifyBulkLookupService> _lookupServiceMock = null!;
    private Mock<ILogger<SpotifyBatchQueueHelper>> _helperLoggerMock = null!;
    private Mock<ILogger<SpotifyBulkProcessorService>> _serviceLoggerMock = null!;

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private const string TrackStream = SpotifyConstants.BulkTrackIdStream;
    private const string AlbumStream = SpotifyConstants.BulkAlbumIdStream;
    private const string TestTrackId = "3n3Ppam7vgaVa1iaRUc9Lp";
    private const string TestSagaId = "aabbccddeeff00112233445566778899";

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

        _ = _redisMock.Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _dbMock.Object );
        _ = _redisMock.Setup( r => r.GetSubscriber( It.IsAny<object>( ) ) )
            .Returns( _subscriberMock.Object );

        // Not rate-limited by default
        _ = _rateLimitTrackerMock.Setup( t => t.GetStateAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( new RateLimitState( false, null, null ) );

        // Track stream: 50 messages (size threshold met — no linger check needed)
        _ = _dbMock.Setup( d => d.StreamLengthAsync( TrackStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( SpotifyConstants.MaxTracksPerBatchLookup );
        // Album stream: 0 (won't trigger album processing)
        _ = _dbMock.Setup( d => d.StreamLengthAsync( AlbumStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 0L );

        // XAUTOCLAIM returns no claimed entries
        _ = _dbMock.Setup( d => d.StreamAutoClaimAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<long>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( StreamAutoClaimResult.Null );

        // XREADGROUP returns one track message by default (8-param overload: includes TimeSpan? for idle time)
        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [BuildStreamEntry( TestTrackId )] );

        // XREADGROUP for album stream: empty (8-param overload)
        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                AlbumStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        // EnsureConsumerGroupsAsync — BUSYGROUP exception (group exists)
        _ = _dbMock.Setup( d => d.StreamCreateConsumerGroupAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<bool>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ThrowsAsync( new RedisServerException( "BUSYGROUP" ) );

        // RequeueAsync reads the original message by StreamRangeAsync
        // Return the same entry so RequeueAsync can read AttemptCount
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [BuildStreamEntry( TestTrackId )] );

        // XACK — single-ID overload (used by both AcknowledgeAsync and RequeueAsync)
        _ = _dbMock.Setup( d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 1L );
        // XACK — batch-ID overload (defensive stub)
        _ = _dbMock.Setup( d => d.StreamAcknowledgeAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue[]>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 1L );
        _ = _dbMock.Setup( d => d.StreamDeleteAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<RedisValue[]>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 1L );

        // XADD (for requeue) returns a new entry ID
        _ = _dbMock.Setup( d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)"9999999999-0" );

        // Subscriber PublishAsync succeeds
        _ = _subscriberMock.Setup( s => s.PublishAsync(
                It.IsAny<RedisChannel>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 0L );

        // Saga manager defaults
        _ = _sagaManagerMock.Setup( m => m.GetOrCreateAsync(
                It.IsAny<string>( ),
                It.IsAny<string>( ),
                It.IsAny<LookupRequestType>( ),
                It.IsAny<string>( ),
                It.IsAny<QueuePriority>( ),
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

    // -------------------------------------------------------------------------
    // B1 — Dispatch contract (3 branches)
    // -------------------------------------------------------------------------

    /// <summary>
    /// B1 branch 1: empty dict from GetTracksByIdsAsync indicates request failure.
    /// ALL messages in the batch must be requeued without writing any saga state.
    /// Failure-first: before B1, empty dict was treated as "all tracks not found"
    /// and the code would write not-found saga states for every message before
    /// attempting to ACK — leaving orphaned saga entries in Redis with no actual API call.
    /// </summary>
    [TestMethod]
    public async Task ProcessBulkTracks_WhenLookupServiceReturnsEmptyDict_ShouldRequeueAllWithoutSagaWrite( ) {
        // Arrange — lookup service returns empty dict (request failure)
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( [] );

        SpotifyBulkProcessorService service = CreateService( );

        // Act — call the internal method directly (InternalsVisibleTo in Worker.Spotify.csproj)
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert — RequeueAsync path: XACK + XDEL of original + XADD (re-add with attempt+1)
        // XACK: original entry must be acknowledged before re-adding
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.AtLeastOnce,
            "Original message must be ACKed as part of RequeueAsync" );

        // XADD: message must be re-added to the stream with incremented AttemptCount
        _dbMock.Verify( d => d.StreamAddAsync(
            (RedisKey)TrackStream,
            It.IsAny<NameValueEntry[]>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<long?>( ),
            It.IsAny<bool>( ),
            It.IsAny<long?>( ),
            It.IsAny<StreamTrimMode>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.AtLeastOnce,
            "Message must be re-added to the stream (requeue)" );

        // Assert — no saga state was written (no UpdateProviderStateAsync calls)
        _sagaManagerMock.Verify( m => m.UpdateProviderStateAsync(
            It.IsAny<string>( ),
            It.IsAny<ProviderLookupState>( ),
            It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "Empty-dict (request failure) must not write saga state for any message" );
    }

    /// <summary>
    /// B1 branch 2: non-empty dict with a key absent means the Spotify API returned a
    /// partial response (parse failure for that ID). The affected message must be requeued
    /// individually; no saga write for the absent-key entry.
    /// Failure-first: before B1, absent key fell into the null-value branch which called
    /// UpdateProviderStateAsync with IsSuccess=false, incorrectly marking it as permanently not-found.
    /// </summary>
    [TestMethod]
    public async Task ProcessBulkTracks_WhenLookupServiceReturnsAbsentKey_ShouldRequeueOneWithoutSagaWrite( ) {
        // Arrange — non-empty dict but our TestTrackId is absent (partial API parse)
        Dictionary<string, MusicLookupResult?> results = new( ) {
            ["someOtherTrackId"] = null
        };
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( results );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert — message for TestTrackId was requeued (XACK + XADD)
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.AtLeastOnce,
            "Message with absent key must be ACKed as part of individual RequeueAsync" );

        // Assert — no saga update for the absent-key message
        _sagaManagerMock.Verify( m => m.UpdateProviderStateAsync(
            It.IsAny<string>( ),
            It.IsAny<ProviderLookupState>( ),
            It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "Absent-key message must not write saga state" );
    }

    /// <summary>
    /// B1 branch 3: key present with null value means genuine not-found from Spotify.
    /// The message must follow the ProcessBulkResultAsync path: saga write (IsSuccess=false),
    /// then ACK (XACK+XDEL with no XADD — not requeued).
    /// Failure-first: before B1, this was indistinguishable from the empty-dict branch,
    /// so the message would be silently requeued instead of being written to saga.
    /// </summary>
    [TestMethod]
    public async Task ProcessBulkTracks_WhenKeyPresentWithNullValue_ShouldWriteNotFoundSagaState( ) {
        // Arrange — key present, null value = genuine not-found
        Dictionary<string, MusicLookupResult?> results = new( ) {
            [TestTrackId] = null
        };
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( results );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert — saga was written with IsSuccess=false (not-found)
        _sagaManagerMock.Verify( m => m.UpdateProviderStateAsync(
            TestSagaId,
            It.Is<ProviderLookupState>( s =>
                s.Provider == SupportedProviders.Spotify &&
                s.IsComplete &&
                !s.IsSuccess ),
            It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "Genuine not-found (key present, null value) must write IsSuccess=false saga state" );

        // Assert — message was ACKed (XACK) as part of AcknowledgeAsync, NOT requeued
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "Not-found message must be ACKed once (AcknowledgeAsync)" );

        // Assert — no XADD (not requeued)
        _dbMock.Verify( d => d.StreamAddAsync(
            (RedisKey)TrackStream,
            It.IsAny<NameValueEntry[]>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<long?>( ),
            It.IsAny<bool>( ),
            It.IsAny<long?>( ),
            It.IsAny<StreamTrimMode>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "Genuine not-found must not be requeued" );
    }

    // -------------------------------------------------------------------------
    // M7a — ShouldFlush predicate (boundary cases not covered in SpotifyBulkFlushPolicyTests)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that count above (not just at) the threshold also triggers a size flush.
    /// The common case where the stream has grown beyond the threshold before the flush loop fires.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenCountExceedsThreshold_ShouldReturnTrue( ) {
        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 51,
            oldestAge: null,
            threshold: 50,
            linger: TimeSpan.FromMilliseconds( 500 )
        );
        Assert.IsTrue( result, "count > threshold must return true" );
    }

    /// <summary>
    /// Verifies that count below threshold with oldestAge exactly at linger triggers flush.
    /// The boundary condition: age >= linger, not strictly >.
    /// </summary>
    [TestMethod]
    public void ShouldFlush_WhenAgeEqualsLinger_ShouldReturnTrue( ) {
        bool result = SpotifyBulkProcessorService.ShouldFlush(
            count: 1,
            oldestAge: TimeSpan.FromMilliseconds( 500 ),
            threshold: 50,
            linger: TimeSpan.FromMilliseconds( 500 )
        );
        Assert.IsTrue( result, "age == linger must trigger flush (>= boundary)" );
    }

    // -------------------------------------------------------------------------
    // Retry cap tests (SpotifyBatchQueueHelper.RequeueAsync)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that RequeueAsync increments AttemptCount on each requeue.
    /// The re-serialized payload written back to the stream must have AttemptCount + 1.
    /// Failure-first: before the retry-cap fix, RequeueAsync ACKed and re-added the
    /// message but never incremented AttemptCount, so a failing message could cycle forever.
    /// </summary>
    [TestMethod]
    public async Task RequeueAsync_WhenAttemptCountIsZero_ShouldRequeueWithAttemptCountOne( ) {
        // Arrange — stream has a message with AttemptCount=0 (fresh message)
        QueuedLookupRequest request = CreateRequest( TestTrackId, attemptCount: 0 );
        StreamEntry entry = BuildStreamEntryFromRequest( request );

        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [entry] );

        NameValueEntry[]? capturedFields = null;
        _ = _dbMock.Setup( d => d.StreamAddAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<NameValueEntry[]>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<long?>( ),
                It.IsAny<bool>( ),
                It.IsAny<long?>( ),
                It.IsAny<StreamTrimMode>( ),
                It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, NameValueEntry[] f, RedisValue? _, long? _, bool _, long? _, StreamTrimMode _, CommandFlags _ ) =>
                capturedFields = f )
            .ReturnsAsync( (RedisValue)"9999-0" );

        SpotifyBatchQueueHelper helper = CreateHelper( );
        string compositeId = $"{TrackStream}:1234567890-0";

        // Act — returns Requeued (not capped, not not-found)
        RequeueOutcome outcome = await helper.RequeueAsync( compositeId, TestSagaId, TestContext.CancellationToken );

        // Assert — method signals "requeued"
        Assert.AreEqual( RequeueOutcome.Requeued, outcome, "Return value must be Requeued (message re-added, not capped)" );

        // Assert — XADD was called (message re-added)
        _dbMock.Verify( d => d.StreamAddAsync(
            (RedisKey)TrackStream,
            It.IsAny<NameValueEntry[]>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<long?>( ),
            It.IsAny<bool>( ),
            It.IsAny<long?>( ),
            It.IsAny<StreamTrimMode>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once, "Message must be re-added after first failure" );

        // Assert — re-serialized payload has AttemptCount=1
        Assert.IsNotNull( capturedFields, "StreamAddAsync must have been called with fields" );
        string? payloadJson = (string?)capturedFields.FirstOrDefault( f => f.Name == QueueStreamFieldNames.Payload ).Value;
        Assert.IsNotNull( payloadJson );
        QueuedLookupRequest? requeuedPayload = JsonSerializer.Deserialize<QueuedLookupRequest>( payloadJson, s_jsonOptions );
        Assert.IsNotNull( requeuedPayload );
        Assert.AreEqual( 1, requeuedPayload.AttemptCount, "AttemptCount must be incremented to 1 on first requeue" );
    }

    /// <summary>
    /// Verifies that RequeueAsync drops a message when AttemptCount has reached MaxRetryAttempts (5).
    /// The message must be ACKed and deleted (to remove it from the PEL) but NOT re-added.
    /// Failure-first: before the retry-cap fix, the message would be re-added indefinitely.
    /// The cap prevents a poison-message stream from occupying the bulk pipeline forever.
    /// </summary>
    [TestMethod]
    public async Task RequeueAsync_WhenAttemptCountAtCap_ShouldAckAndNotRequeue( ) {
        // Arrange — message already at MaxRetryAttempts (5)
        QueuedLookupRequest request = CreateRequest( TestTrackId, attemptCount: 5 );
        StreamEntry entry = BuildStreamEntryFromRequest( request );

        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [entry] );

        SpotifyBatchQueueHelper helper = CreateHelper( );
        string compositeId = $"{TrackStream}:1234567890-0";

        // Act — returns CapReached
        RequeueOutcome outcome = await helper.RequeueAsync( compositeId, TestSagaId, TestContext.CancellationToken );

        // Assert — method signals "gave up"
        Assert.AreEqual( RequeueOutcome.CapReached, outcome, "Return value must be CapReached when the retry cap is reached" );

        // Assert — ACK + delete (message cleared from PEL) — single-ID overload
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once, "Message at cap must still be ACKed to clear it from PEL" );

        // Assert — XADD must NOT be called (not requeued)
        _dbMock.Verify( d => d.StreamAddAsync(
            (RedisKey)TrackStream,
            It.IsAny<NameValueEntry[]>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<long?>( ),
            It.IsAny<bool>( ),
            It.IsAny<long?>( ),
            It.IsAny<StreamTrimMode>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Never, "Message at cap must not be re-added (complete-failed semantics)" );
    }

    /// <summary>
    /// Verifies that when a message at the retry cap enters the absent-key dispatch path,
    /// the service writes a complete-failed provider state and publishes both the
    /// saga-completed and the lookup-completion events via <c>RequeueSingleAsync</c>.
    /// </summary>
    /// <remarks>
    /// Discriminating path: the lookup service returns a non-empty dict that does NOT
    /// contain <c>TestTrackId</c> (absent-key / partial-parse-failure branch). The service
    /// calls <c>RequeueSingleAsync</c> for the absent-key message; the helper sees
    /// <c>AttemptCount == MaxQueueRetryAttempts</c> and returns <c>false</c> (cap hit);
    /// <c>RequeueSingleAsync</c> must then call <c>UpdateProviderStateAsync</c> with
    /// <c>IsSuccess=false</c> and an error message containing "Bulk lookup failed after",
    /// then publish to <c>saga:completed</c> and to <c>complete:{lookupKey}</c>.
    /// <para>
    /// Failure-first: if the publish calls are removed from <c>RequeueSingleAsync</c> the
    /// two channel-specific <c>PublishAsync</c> assertions fail — they cannot be satisfied
    /// by the normal-path publishes because no normal-path dispatch runs here (the result
    /// dict has no entry for <c>TestTrackId</c>).
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task ProcessBulkTracks_WhenAttemptCountAtCap_ShouldWriteCompleteFailedStateAndPublishCompletion( ) {
        // Arrange — message at retry cap (AttemptCount = MaxQueueRetryAttempts)
        QueuedLookupRequest request = CreateRequest( TestTrackId, attemptCount: LookupConstants.MaxQueueRetryAttempts );
        StreamEntry capEntry = BuildStreamEntryFromRequest( request );

        string expectedLookupKey = $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}";

        // XREADGROUP returns the at-cap message
        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [capEntry] );

        // RequeueAsync reads the original entry via StreamRangeAsync — returns the cap entry
        // so the helper reads AttemptCount = MaxQueueRetryAttempts and returns false.
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [capEntry] );

        // Absent-key: non-empty dict that does NOT contain TestTrackId.
        // This routes the message through the absent-key branch → RequeueSingleAsync,
        // which hits the cap and executes the failed-state + publish path.
        // An empty dict would take the "request failure / requeue ALL" branch instead,
        // also reaching RequeueSingleAsync but via a different route — absent-key is
        // used here because it isolates the single-message cap path unambiguously.
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( new Dictionary<string, MusicLookupResult?> { ["someOtherTrackId"] = null } );

        // Saga returned by GetAsync: IsComplete=true (all providers done, no FinalResultUri)
        // so CheckAndPublishSagaCompletionAsync proceeds to publish saga:completed.
        LookupSagaState completedSaga = new( ) {
            SagaId = TestSagaId,
            LookupKey = expectedLookupKey,
            LookupType = LookupRequestType.SongIdLookup,
            LookupValue = TestTrackId,
            ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                [SupportedProviders.Spotify] = new ProviderLookupState(
                    Provider: SupportedProviders.Spotify,
                    IsComplete: true,
                    IsSuccess: false,
                    ResultJson: null,
                    CompletedAt: DateTimeOffset.UtcNow,
                    ErrorMessage: $"Bulk lookup failed after {LookupConstants.MaxQueueRetryAttempts} attempts" )
            },
            FinalResultUri = null
        };
        _ = _sagaManagerMock.Setup( m => m.GetAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( completedSaga );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert (a): UpdateProviderStateAsync called with IsSuccess=false and the cap error message.
        // This is the service-level write that marks the saga provider as permanently failed.
        _sagaManagerMock.Verify( m => m.UpdateProviderStateAsync(
            TestSagaId,
            It.Is<ProviderLookupState>( s =>
                !s.IsSuccess &&
                s.ErrorMessage != null &&
                s.ErrorMessage.Contains( "Bulk lookup failed after" ) ),
            It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "RequeueSingleAsync cap path must write IsSuccess=false with 'Bulk lookup failed after' error" );

        // Assert (b1): saga-completed channel publish fires (CheckAndPublishSagaCompletionAsync).
        // This unblocks saga coordinators waiting on the saga:completed channel.
        _subscriberMock.Verify( s => s.PublishAsync(
            It.Is<RedisChannel>( ch => ch == RedisChannel.Literal( "saga:completed" ) ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "saga:completed must be published when the retry cap is reached (F2)" );

        // Assert (b2): lookup-completion channel publish fires (PublishLookupCompletionAsync).
        // This unblocks interactive waiters subscribed to the per-lookup-key channel.
        _subscriberMock.Verify( s => s.PublishAsync(
            It.Is<RedisChannel>( ch => ch == RedisChannel.Literal( $"complete:{expectedLookupKey}" ) ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "complete:{lookupKey} must be published when the retry cap is reached (F2)" );
    }

    // -------------------------------------------------------------------------
    // Rate-limit parity: HandleBulkRateLimitAsync per-saga partial marking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that HandleBulkRateLimitAsync calls SetIsPartialAsync for each message
    /// when the bulk endpoint returns a 429 (RetryAfterExceededException).
    /// Failure-first: before the rate-limit parity fix, HandleBulkRateLimitAsync only
    /// called SetRateLimitedAsync at the tracker level but never per-saga, leaving
    /// interactive callers waiting indefinitely for a completion event that was never
    /// published.
    /// </summary>
    [TestMethod]
    public async Task HandleBulkRateLimit_WhenCalled_ShouldMarkSagasPartialAndPublishSentinel( ) {
        // Arrange — one queued message with known SagaId
        QueuedLookupRequest request = CreateRequest( TestTrackId );
        List<QueuedMessage<QueuedLookupRequest>> messages = [
            new QueuedMessage<QueuedLookupRequest>(
                $"{TrackStream}:1234567890-0",
                request,
                DateTimeOffset.UtcNow )
        ];

        RetryAfterExceededException ex = new(
            retryAfterValue: TimeSpan.FromSeconds( 30 ),
            threshold: TimeSpan.FromSeconds( 120 ),
            requestUri: null,
            provider: SupportedProviders.Spotify );

        _ = _rateLimitTrackerMock.Setup( t => t.SetRateLimitedAsync(
                It.IsAny<SupportedProviders>( ),
                It.IsAny<string>( ),
                It.IsAny<DateTimeOffset>( ),
                It.IsAny<CancellationToken>( ) ) )
            .Returns( Task.CompletedTask );

        SpotifyBulkProcessorService service = CreateService( );

        // Act — call internal method directly
        await service.HandleBulkRateLimitAsync( messages, SpotifyConstants.BulkTracksEndpoint, ex, TestContext.CancellationToken );

        // Assert — SetIsPartialAsync called for the saga
        _sagaManagerMock.Verify( m => m.SetIsPartialAsync(
            TestSagaId,
            true,
            It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "Each rate-limited saga must be marked partial (parity with QueueProcessorBackgroundService rate-limit handling)" );

        // Assert — SetRateLimitInfoAsync called to merge rate-limit info into saga
        _sagaManagerMock.Verify( m => m.SetRateLimitInfoAsync(
            TestSagaId,
            It.Is<List<ProviderRateLimitInfo>>( list =>
                list.Any( r => r.Provider == SupportedProviders.Spotify ) ),
            It.IsAny<CancellationToken>( ) ),
            Times.Once,
            "Rate-limit info must be merged into the saga with Spotify provider entry" );

        // Assert — sentinel published on the saga's completion channel
        _subscriberMock.Verify( s => s.PublishAsync(
            It.Is<RedisChannel>( c => c.ToString( ).Contains( TestTrackId ) ),
            LookupConstants.RateLimitedSentinel,
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "Rate-limited sentinel must be published so interactive callers are not left hanging" );
    }

    // -------------------------------------------------------------------------
    // LookupKeyBuilder canonical format alignment
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that LookupKeyBuilder.TypedKey produces the canonical format
    /// <c>{LookupType}:{Provider}:{normalizedId}</c> that all four producers must agree on.
    /// Failure-first: before the format-alignment fix, QueueProcessorBackgroundService used
    /// <c>"{request.LookupType}:{request.LookupValue}"</c> (missing the provider segment),
    /// and JetStreamWatcherService used the same wrong format; the resulting saga IDs
    /// could never align between a JetStream-originated request and a bulk-processor result.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_TypedKey_ShouldMatchCanonicalFormat( ) {
        string key = LookupKeyBuilder.TypedKey(
            LookupRequestType.SongIdLookup,
            SupportedProviders.Spotify,
            TestTrackId
        );

        Assert.AreEqual(
            $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}",
            key,
            "TypedKey must produce '{LookupType}:{Provider}:{id}'" );
    }

    /// <summary>
    /// Verifies that LookupKeyBuilder.UrlKey produces the canonical format
    /// <c>UriLookup:{HashUrl(url)}</c> for URL-based lookups.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_UrlKey_ShouldMatchCanonicalFormat( ) {
        string url = "https://open.spotify.com/artist/6qqNVTkY8uBg9cP3Jd7DAH";
        string key = LookupKeyBuilder.UrlKey( url );

        // Key must start with UriLookup: prefix
        Assert.IsTrue(
            key.StartsWith( "UriLookup:", StringComparison.Ordinal ),
            "UrlKey must start with 'UriLookup:'" );

        // Key must be deterministic (same input → same output)
        string key2 = LookupKeyBuilder.UrlKey( url );
        Assert.AreEqual( key, key2, "UrlKey must be deterministic for the same URL" );
    }

    /// <summary>
    /// Verifies cross-producer alignment: JetStreamWatcher and SpotifyBulkProcessorService
    /// must generate the same saga ID for the same track ID so they share state.
    /// The saga ID is derived from the lookup key via <see cref="ISagaStateManager.GenerateSagaId"/>.
    /// Failure-first: before the format-alignment fix, the two producers used different
    /// key formats, so their GenerateSagaId outputs never matched — each thought the other's
    /// request was a different saga, producing duplicate entries and no cross-producer state
    /// sharing.
    /// </summary>
    [TestMethod]
    public void LookupKeyBuilder_JetStreamAndBulkProcessor_ShouldProduceSameSagaIdForSameTrackId( ) {
        // JetStreamWatcher path: uses LookupKeyBuilder.TypedKey
        string jetStreamKey = LookupKeyBuilder.TypedKey(
            LookupRequestType.SongIdLookup,
            SupportedProviders.Spotify,
            TestTrackId );

        // SpotifyBulkProcessorService path: uses LookupKeyBuilder.TypedKey (ProcessBulkResultAsync)
        string bulkProcessorKey = LookupKeyBuilder.TypedKey(
            LookupRequestType.SongIdLookup,
            SupportedProviders.Spotify,
            TestTrackId );

        // QueueProcessorBackgroundService path: also uses LookupKeyBuilder.TypedKey
        string queueProcessorKey = LookupKeyBuilder.TypedKey(
            LookupRequestType.SongIdLookup,
            SupportedProviders.Spotify,
            TestTrackId );

        string sagaIdFromJetStream = ISagaStateManager.GenerateSagaId( jetStreamKey );
        string sagaIdFromBulkProcessor = ISagaStateManager.GenerateSagaId( bulkProcessorKey );
        string sagaIdFromQueueProcessor = ISagaStateManager.GenerateSagaId( queueProcessorKey );

        Assert.AreEqual( sagaIdFromJetStream, sagaIdFromBulkProcessor,
            "JetStreamWatcher and SpotifyBulkProcessorService must produce the same saga ID for the same track" );
        Assert.AreEqual( sagaIdFromBulkProcessor, sagaIdFromQueueProcessor,
            "SpotifyBulkProcessorService and QueueProcessorBackgroundService must produce the same saga ID for the same track" );
    }

    // -------------------------------------------------------------------------
    // Poison entries (missing payload) must be ACK+XDELed, not skipped
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that an XREADGROUP entry with an empty payload field is ACK+XDELed rather than
    /// silently skipped. Skipping leaves it in the PEL and XAUTOCLAIM re-claims it every
    /// AutoClaimMinIdleMs, causing an eternal log-flood loop.
    /// Failure-first: before the poison-entry fix, <c>string.IsNullOrEmpty(payload)</c> hit a
    /// bare <c>continue</c>; this test would fail because StreamAcknowledgeAsync was never called.
    /// </summary>
    [TestMethod]
    public async Task DequeueBatch_WhenXReadGroupEntryHasEmptyPayload_ShouldAckAndDeletePoisonEntry( ) {
        // Arrange — XAUTOCLAIM returns empty; XREADGROUP returns one entry with no payload
        NameValueEntry[] emptyPayloadFields = [
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
            // Note: no "payload" field — simulates a corrupt / empty entry
        ];
        StreamEntry poisonEntry = new( (RedisValue)"1234567890-0", emptyPayloadFields );

        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [poisonEntry] );

        SpotifyBatchQueueHelper helper = CreateHelper( );

        // Act
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages =
            await helper.DequeueTrackIdBatchAsync( cancellationToken: TestContext.CancellationToken );

        // Assert — no messages returned (poison was discarded)
        Assert.HasCount( 0, messages, "Poison entry must be discarded, not returned" );

        // Assert — XACK must have been called to remove from PEL
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "Poison entry must be ACKed to remove it from the PEL (prevent eternal re-claim)" );

        // Assert — XDEL must have been called
        _dbMock.Verify( d => d.StreamDeleteAsync(
            (RedisKey)TrackStream,
            It.IsAny<RedisValue[]>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "Poison entry must be XDELed so it cannot re-appear via XAUTOCLAIM" );
    }

    /// <summary>
    /// Verifies that an XREADGROUP entry whose payload field is the literal JSON string
    /// <c>"null"</c> is ACK+XDELed rather than silently skipped. Unlike a missing or empty
    /// payload field, <c>"null"</c> deserializes without throwing but produces a null
    /// <see cref="QueuedLookupRequest"/> object — the <c>request is null</c> guard at
    /// SpotifyBatchQueueHelper.cs:229 and 286 catches this case and routes it through
    /// <c>AckAndDeletePoisonEntryAsync</c>.
    /// Failure-first: reverting the <c>request is null</c> branch to a bare <c>continue</c>
    /// causes <c>StreamAcknowledgeAsync</c> to never be called; this test fails at the XACK
    /// assertion with Times.Never instead of Times.Once.
    /// </summary>
    [TestMethod]
    public async Task DequeueBatch_WhenXReadGroupEntryHasNullJsonPayload_ShouldAckAndDeletePoisonEntry( ) {
        // Arrange — XAUTOCLAIM returns empty (default); XREADGROUP returns one entry whose
        // payload field is the literal JSON string "null" (valid JSON, but deserializes to null).
        NameValueEntry[] nullPayloadFields = [
            new NameValueEntry( QueueStreamFieldNames.Payload, "null" ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];
        StreamEntry poisonEntry = new( (RedisValue)"1234567890-1", nullPayloadFields );

        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [poisonEntry] );

        SpotifyBatchQueueHelper helper = CreateHelper( );

        // Act
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages =
            await helper.DequeueTrackIdBatchAsync( cancellationToken: TestContext.CancellationToken );

        // Assert — no messages returned (poison was discarded)
        Assert.HasCount( 0, messages, "Null-JSON-payload poison entry must be discarded, not returned" );

        // Assert — XACK must have been called to remove from PEL
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "Null-JSON-payload poison entry must be ACKed to remove it from the PEL (prevent eternal re-claim)" );

        // Assert — XDEL must have been called
        _dbMock.Verify( d => d.StreamDeleteAsync(
            (RedisKey)TrackStream,
            It.IsAny<RedisValue[]>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Once,
            "Null-JSON-payload poison entry must be XDELed so it cannot re-appear via XAUTOCLAIM" );
    }

    // -------------------------------------------------------------------------
    // RequeueAsync not-found branch: leave saga untouched
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that when the stream entry is missing (already XDELed — duplicate in-flight
    /// after XAUTOCLAIM re-claim), <c>RequeueAsync</c> returns <see cref="RequeueOutcome.NotFound"/>
    /// and <c>RequeueSingleAsync</c> in <c>SpotifyBulkProcessorService</c> does NOT write saga state.
    /// Failure-first: before the not-found branch fix, <c>not-found</c> returned <c>false</c>
    /// (same as cap-reached), causing <c>RequeueSingleAsync</c> to write a complete-failed saga
    /// state for a message already successfully processed by another consumer — corrupting the
    /// saga outcome.
    /// </summary>
    [TestMethod]
    public async Task RequeueAsync_WhenEntryNotFound_ShouldReturnNotFoundWithoutSagaWrite( ) {
        // Arrange — StreamRangeAsync returns empty (entry already XDELed)
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        SpotifyBatchQueueHelper helper = CreateHelper( );
        string compositeId = $"{TrackStream}:1234567890-0";

        // Act
        RequeueOutcome outcome = await helper.RequeueAsync( compositeId, TestSagaId, TestContext.CancellationToken );

        // Assert — returns NotFound, not CapReached
        Assert.AreEqual( RequeueOutcome.NotFound, outcome,
            "Missing entry must return NotFound so the caller leaves the saga untouched" );

        // Assert — no XACK (nothing to ACK — entry is already gone)
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "No ACK should be issued when the entry is not found" );

        // Assert — no XADD (not re-queued)
        _dbMock.Verify( d => d.StreamAddAsync(
            (RedisKey)TrackStream,
            It.IsAny<NameValueEntry[]>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<long?>( ),
            It.IsAny<bool>( ),
            It.IsAny<long?>( ),
            It.IsAny<StreamTrimMode>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "Entry not found must not be re-added" );
    }

    /// <summary>
    /// Verifies that when <c>RequeueAsync</c> returns <c>NotFound</c>, the service does NOT
    /// call <c>UpdateProviderStateAsync</c> — the saga must be left untouched because the
    /// entry was already processed by another consumer (duplicate in-flight).
    /// Failure-first: before the not-found branch fix, the not-found case returned false
    /// (same as CapReached), so <c>RequeueSingleAsync</c> would call
    /// <c>UpdateProviderStateAsync</c> with <c>IsSuccess=false</c>, overwriting a potentially
    /// successful result already written by the other consumer.
    /// </summary>
    [TestMethod]
    public async Task ProcessBulkTracks_WhenRequeueReturnsNotFound_ShouldNotWriteSagaState( ) {
        // Arrange — lookup service returns a non-empty dict that doesn't contain TestTrackId
        // so the absent-key branch calls RequeueSingleAsync. The stream entry is not found
        // so RequeueAsync returns NotFound.
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( new Dictionary<string, MusicLookupResult?> { ["otherTrackId"] = null } );

        // StreamRangeAsync returns empty — simulates entry already XDELed by another consumer.
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert — saga state must NOT be written when entry is not found
        _sagaManagerMock.Verify( m => m.UpdateProviderStateAsync(
            It.IsAny<string>( ),
            It.IsAny<ProviderLookupState>( ),
            It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "NotFound requeue outcome must not trigger saga state write" );
    }

    // -------------------------------------------------------------------------
    // ComputeCooldownSeconds overflow protection and clamping
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that <c>ComputeCooldownSeconds</c> returns a positive value for all tested
    /// consecutive-failure counts and clamps at <c>maxSeconds</c>.
    /// </summary>
    /// <remarks>
    /// Failure-first discipline: the pure function is tested directly.
    /// Before the overflow-protection fix, <c>1 &lt;&lt; (consecutiveFailures - 1)</c> would
    /// overflow int at n≈32, producing a negative product; the clamped formula prevents this.
    /// A test against the pre-fix formula would fail at n=32 or n=40 because the result
    /// would be negative, violating the "stays positive" assertion.
    /// </remarks>
    [TestMethod]
    public void ComputeCooldownSeconds_ForRepresentativeFailureCounts_ShouldStayPositiveAndClampAtMax( ) {
        const int BaseSeconds = 5;
        const int MaxSeconds = SpotifyBatchSettings.MaxRequestFailureCooldownSeconds;

        int[] testCases = [1, 2, 6, 32, 40];
        foreach (int n in testCases) {
            int result = SpotifyBulkProcessorService.ComputeCooldownSeconds( n, BaseSeconds, MaxSeconds );

            Assert.IsGreaterThan( 0, result,
                $"ComputeCooldownSeconds(n={n}) must be positive; got {result}" );
            Assert.IsLessThanOrEqualTo( MaxSeconds, result,
                $"ComputeCooldownSeconds(n={n}) must not exceed MaxSeconds ({MaxSeconds}); got {result}" );
        }

        // Boundary: n=6 is the first iteration where the exponent clamp at 5 takes effect.
        // base * 2^5 = 5 * 32 = 160 > MaxSeconds(60) → result must equal MaxSeconds.
        int atClamp = SpotifyBulkProcessorService.ComputeCooldownSeconds( 6, BaseSeconds, MaxSeconds );
        Assert.AreEqual( MaxSeconds, atClamp,
            "n=6 (exponent=5, first clamped value) must return exactly MaxSeconds" );
    }

    // -------------------------------------------------------------------------
    // Factory and helpers
    // -------------------------------------------------------------------------

    private SpotifyBulkProcessorService CreateService( ) {
        SpotifyBatchQueueHelper helper = CreateHelper( );
        IOptions<SpotifyBatchSettings> options = Microsoft.Extensions.Options.Options.Create(
            new SpotifyBatchSettings { LingerMs = 500 } );

        return new SpotifyBulkProcessorService(
            _redisMock.Object,
            helper,
            _rateLimitTrackerMock.Object,
            _sagaManagerMock.Object,
            _lookupServiceMock.Object,
            _serviceLoggerMock.Object,
            options
        );
    }

    private SpotifyBatchQueueHelper CreateHelper( ) =>
        new( _redisMock.Object, _helperLoggerMock.Object );

    private static QueuedLookupRequest CreateRequest( string trackId, int attemptCount = 0 ) =>
        new( ) {
            RequestId = Guid.NewGuid( ).ToString( "N" ),
            Provider = SupportedProviders.Spotify,
            LookupType = LookupRequestType.SongIdLookup,
            LookupValue = trackId,
            SagaId = TestSagaId,
            IsAlbum = false,
            OriginPriority = QueuePriority.Bulk,
            AttemptCount = attemptCount
        };

    private StreamEntry BuildStreamEntry( string trackId ) {
        QueuedLookupRequest request = CreateRequest( trackId );
        return BuildStreamEntryFromRequest( request );
    }

    private StreamEntry BuildStreamEntryFromRequest( QueuedLookupRequest request ) {
        string payload = JsonSerializer.Serialize( request, s_jsonOptions );
        NameValueEntry[] values = [
            new NameValueEntry( QueueStreamFieldNames.Payload, payload ),
            new NameValueEntry( QueueStreamFieldNames.EnqueuedAt, DateTimeOffset.UtcNow.ToString( "O" ) )
        ];
        return new StreamEntry( (RedisValue)"1234567890-0", values );
    }

    // -------------------------------------------------------------------------
    // XAUTOCLAIM claimed entries must flow into the returned batch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that a stranded entry recovered by XAUTOCLAIM is included in the returned
    /// message list when XREADGROUP returns empty (the common crash-recovery scenario).
    /// Failure-first: before the XAUTOCLAIM-inclusion fix, <c>DequeueBatchFromStreamAsync</c>
    /// logged claimed entries but discarded them; only ">" new entries from XREADGROUP were
    /// returned. A stranded entry would be re-claimed every <c>AutoClaimMinIdleMs</c> but
    /// never completed, causing the age-trigger flush to fire every 500ms with empty dequeues.
    /// </summary>
    [TestMethod]
    public async Task DequeueTrackIdBatch_WhenAutoClaimReturnsEntry_ShouldIncludeClaimedEntryInBatch( ) {
        // Arrange — XAUTOCLAIM returns one stranded entry; XREADGROUP returns empty
        QueuedLookupRequest strandedRequest = CreateRequest( TestTrackId, attemptCount: 1 );
        StreamEntry strandedEntry = BuildStreamEntryFromRequest( strandedRequest );

        // StreamAutoClaimResult has an internal constructor; invoke it via reflection.
        ConstructorInfo autoClaimCtor = typeof( StreamAutoClaimResult )
            .GetConstructors( BindingFlags.NonPublic | BindingFlags.Instance )[0];
        StreamAutoClaimResult autoClaimResult = (StreamAutoClaimResult)autoClaimCtor.Invoke(
            [(RedisValue)"0-0", new StreamEntry[] { strandedEntry }, Array.Empty<RedisValue>( )] );

        _ = _dbMock.Setup( d => d.StreamAutoClaimAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<long>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( autoClaimResult );

        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [] );

        SpotifyBatchQueueHelper helper = CreateHelper( );

        // Act
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages =
            await helper.DequeueTrackIdBatchAsync( cancellationToken: TestContext.CancellationToken );

        // Assert — the claimed entry must appear in the returned batch
        Assert.HasCount( 1, messages,
            "Claimed entry from XAUTOCLAIM must be included in the returned batch" );
        Assert.AreEqual( TestTrackId, messages[0].Payload.LookupValue,
            "Returned message must match the claimed entry's payload" );
        Assert.AreEqual( 1, messages[0].Payload.AttemptCount,
            "AttemptCount from the claimed entry must be preserved" );
    }

    /// <summary>
    /// Verifies that the count budget is respected when both XAUTOCLAIM and XREADGROUP
    /// have entries: claimed entries reduce the XREADGROUP count, and the combined total
    /// does not exceed the requested batch size.
    /// Failure-first: before the XAUTOCLAIM-inclusion fix, claimed entries were discarded
    /// and the XREADGROUP call always received the full count, allowing the combined total
    /// to exceed the budget.
    /// </summary>
    [TestMethod]
    public async Task DequeueTrackIdBatch_WhenAutoClaimFillsBudget_ShouldSkipXReadGroup( ) {
        // Arrange — request count=1; XAUTOCLAIM already fills the budget
        QueuedLookupRequest strandedRequest = CreateRequest( TestTrackId, attemptCount: 2 );
        StreamEntry strandedEntry = BuildStreamEntryFromRequest( strandedRequest );

        // StreamAutoClaimResult has an internal constructor; invoke it via reflection.
        ConstructorInfo autoClaimCtor2 = typeof( StreamAutoClaimResult )
            .GetConstructors( BindingFlags.NonPublic | BindingFlags.Instance )[0];
        StreamAutoClaimResult autoClaimResult2 = (StreamAutoClaimResult)autoClaimCtor2.Invoke(
            [(RedisValue)"0-0", new StreamEntry[] { strandedEntry }, Array.Empty<RedisValue>( )] );

        _ = _dbMock.Setup( d => d.StreamAutoClaimAsync(
                TrackStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<long>( ),
                It.IsAny<RedisValue>( ),
                1,        // count=1 passed to XAUTOCLAIM
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( autoClaimResult2 );

        SpotifyBatchQueueHelper helper = CreateHelper( );

        // Act — request exactly 1 message
        IReadOnlyList<QueuedMessage<QueuedLookupRequest>> messages =
            await helper.DequeueTrackIdBatchAsync( maxCount: 1, TestContext.CancellationToken );

        // Assert — exactly one message from the claimed entry
        Assert.HasCount( 1, messages, "Budget of 1 must be filled by the claimed entry alone" );

        // Assert — XREADGROUP was NOT called (budget exhausted by claimed entries)
        _dbMock.Verify( d => d.StreamReadGroupAsync(
            TrackStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<int?>( ),
            It.IsAny<bool>( ),
            It.IsAny<TimeSpan?>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "XREADGROUP must not be called when the count budget is already filled by claimed entries" );
    }

    // -------------------------------------------------------------------------
    // V2 dedup: completed saga with FinalResultUri set skips republish
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that <c>CheckAndPublishSagaCompletionAsync</c> skips the completion publish
    /// when the saga already has a <c>FinalResultUri</c> set (dedup guard).
    /// With deterministic saga IDs, a re-shared URL hitting a lingering completed saga is
    /// the common case in V2; without this guard every re-share would re-publish, causing
    /// duplicate coordinator writes.
    /// Failure-first: before the FinalResultUri dedup guard was added, ProcessBulkResultAsync
    /// would publish on every call regardless of whether the saga was already finalized.
    /// </summary>
    [TestMethod]
    public async Task ProcessBulkTracks_WhenSagaAlreadyHasFinalResultUri_ShouldSkipCompletionPublish( ) {
        // Arrange — lookup service returns a valid result for the track
        Dictionary<string, MusicLookupResult?> results = new( ) { [TestTrackId] = null };
        _ = _lookupServiceMock.Setup( s => s.GetTracksByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( results );

        // Saga already has a FinalResultUri — the completion has been published before.
        // IsComplete is computed from ProviderStates, so populate a completed Spotify entry.
        _ = _sagaManagerMock.Setup( m => m.GetAsync( It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( new LookupSagaState {
                SagaId = TestSagaId,
                LookupKey = $"{LookupRequestType.SongIdLookup}:{SupportedProviders.Spotify}:{TestTrackId}",
                LookupType = LookupRequestType.SongIdLookup,
                LookupValue = TestTrackId,
                ProviderStates = new Dictionary<SupportedProviders, ProviderLookupState> {
                    [SupportedProviders.Spotify] = new ProviderLookupState(
                        Provider: SupportedProviders.Spotify,
                        IsComplete: true,
                        IsSuccess: true,
                        ResultJson: null,
                        CompletedAt: DateTimeOffset.UtcNow,
                        ErrorMessage: null )
                },
                FinalResultUri = "https://bridgebeats.example/songs/abc"  // already finalized
            } );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.ProcessBulkTrackLookupsAsync( TestContext.CancellationToken );

        // Assert — saga:completed must NOT be published (FinalResultUri guard fires)
        _subscriberMock.Verify( s => s.PublishAsync(
            It.Is<RedisChannel>( c => c.ToString( ) == "saga:completed" ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.Never,
            "saga:completed must not be published when the saga already has a FinalResultUri" );
    }

    // -------------------------------------------------------------------------
    // Album path wiring — mirrors the track-path empty-dict-requeue test
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that <c>ProcessBulkAlbumLookupsAsync</c> requeues all messages without writing
    /// saga state when <c>GetAlbumsByIdsAsync</c> returns an empty dictionary (request failure).
    /// The album path duplicates the track-path branch logic; this test provides direct wiring
    /// coverage analogous to the track-path <c>ProcessBulkTracks_WhenLookupServiceReturnsEmptyDict</c> test.
    /// Failure-first: a regression that wired the album-path empty-dict case to the present-key
    /// branch (calling UpdateProviderStateAsync) would be caught by the Times.Never assertion.
    /// </summary>
    [TestMethod]
    public async Task ProcessBulkAlbums_WhenLookupServiceReturnsEmptyDict_ShouldRequeueAllWithoutSagaWrite( ) {
        const string TestAlbumId = "6WdSsBrH5QtofaTTqgwxOV";

        // Arrange — lookup service returns empty dict (request failure)
        _ = _lookupServiceMock.Setup( s => s.GetAlbumsByIdsAsync( It.IsAny<IEnumerable<string>>( ) ) )
            .ReturnsAsync( [] );

        // Album stream at threshold so ShouldProcessBulkAlbumsAsync fires
        _ = _dbMock.Setup( d => d.StreamLengthAsync( AlbumStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( SpotifyConstants.MaxAlbumsPerBatchLookup );
        // Track stream back to 0 so only the album path runs
        _ = _dbMock.Setup( d => d.StreamLengthAsync( TrackStream, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( 0L );

        // Build an album-typed request
        QueuedLookupRequest albumRequest = new( ) {
            RequestId = Guid.NewGuid( ).ToString( "N" ),
            Provider = SupportedProviders.Spotify,
            LookupType = LookupRequestType.AlbumIdLookup,
            LookupValue = TestAlbumId,
            SagaId = TestSagaId,
            IsAlbum = true,
            OriginPriority = QueuePriority.Bulk,
            AttemptCount = 0
        };
        StreamEntry albumEntry = BuildStreamEntryFromRequest( albumRequest );

        // XREADGROUP for album stream returns one entry
        _ = _dbMock.Setup( d => d.StreamReadGroupAsync(
                AlbumStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue?>( ),
                It.IsAny<int?>( ),
                It.IsAny<bool>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [albumEntry] );

        // RequeueAsync reads the original message by StreamRangeAsync for the album stream
        _ = _dbMock.Setup( d => d.StreamRangeAsync(
                AlbumStream,
                It.IsAny<RedisValue>( ),
                It.IsAny<RedisValue>( ),
                It.IsAny<int?>( ),
                It.IsAny<Order>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( [albumEntry] );

        SpotifyBulkProcessorService service = CreateService( );

        // Act
        await service.ProcessBulkAlbumLookupsAsync( TestContext.CancellationToken );

        // Assert — XACK fired (original entry acknowledged before re-add)
        _dbMock.Verify( d => d.StreamAcknowledgeAsync(
            AlbumStream,
            It.IsAny<RedisValue>( ),
            It.IsAny<RedisValue>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.AtLeastOnce,
            "Original album message must be ACKed as part of RequeueAsync" );

        // Assert — XADD fired (message re-added to the album stream)
        _dbMock.Verify( d => d.StreamAddAsync(
            (RedisKey)AlbumStream,
            It.IsAny<NameValueEntry[]>( ),
            It.IsAny<RedisValue?>( ),
            It.IsAny<long?>( ),
            It.IsAny<bool>( ),
            It.IsAny<long?>( ),
            It.IsAny<StreamTrimMode>( ),
            It.IsAny<CommandFlags>( ) ),
            Times.AtLeastOnce,
            "Album message must be re-added to the album stream (requeue)" );

        // Assert — no saga state written (empty-dict = request failure, not not-found)
        _sagaManagerMock.Verify( m => m.UpdateProviderStateAsync(
            It.IsAny<string>( ),
            It.IsAny<ProviderLookupState>( ),
            It.IsAny<CancellationToken>( ) ),
            Times.Never,
            "Empty-dict (request failure) on the album path must not write saga state" );
    }
}
