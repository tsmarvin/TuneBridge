using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StatisticsService"/>, which computes lookup statistics by enumerating
/// AT Protocol records and caches the result. Covers the cached-read path (null before any refresh,
/// populated after), the read-only <c>GetStatisticsAsync</c> contract that never triggers
/// computation, refresh triggering and the single-flight guard via <c>IsRefreshing</c>, cache
/// freshness (skip when fresh, recompute on force), record counting and album/track classification,
/// and the live bootstrap-status read from Redis including its null and exception-swallowing paths.
/// </summary>
[TestClass]
public class StatisticsServiceTests {

    /// <summary>Mock storage service that supplies the record stream the service aggregates.</summary>
    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    /// <summary>Mock Redis multiplexer providing the database used for bootstrap-status reads.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock Redis database backing bootstrap-status string reads.</summary>
    private Mock<IDatabase> _redisDatabaseMock = null!;
    /// <summary>Mock logger for the service.</summary>
    private Mock<ILogger<StatisticsService>> _loggerMock = null!;
    /// <summary>Statistics settings (PDS URI, user DID, refresh interval, cache TTL) under test.</summary>
    private StatisticsSettings _settings = null!;

    /// <summary>MSTest-injected context; provides per-test cancellation tokens.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>Test PDS URI the service enumerates records from.</summary>
    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    /// <summary>Test user DID whose repository is enumerated.</summary>
    private const string TestUserDid = "did:plc:testuser123";

    /// <summary>
    /// Creates fresh mocks before each test, defaults Redis bootstrap-status reads to null, and
    /// configures settings with a six-hour refresh interval and a thirty-second cache TTL.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _redisDatabaseMock = new Mock<IDatabase>( );
        _ = _redisMock.Setup( x => x.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) ).Returns( _redisDatabaseMock.Object );
        _ = _redisDatabaseMock.Setup( x => x.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) ).ReturnsAsync( RedisValue.Null );
        _loggerMock = new Mock<ILogger<StatisticsService>>( );
        _settings = new StatisticsSettings(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 6 ),
            TimeSpan.FromSeconds( 30 )
        );
    }

    /// <summary>
    /// Verifies that <c>GetCachedStatistics</c> returns null before any refresh has populated the
    /// cache.
    /// </summary>
    [TestMethod]
    public void GetCachedStatistics_WhenNoCachedData_ReturnsNull( ) {
        StatisticsService service = CreateService( );

        LookupStatistics? result = service.GetCachedStatistics();

        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that after a refresh over an empty record set, <c>GetCachedStatistics</c> returns a
    /// non-null result reporting zero total records.
    /// </summary>
    [TestMethod]
    public async Task GetCachedStatistics_AfterRefresh_ReturnsCachedData( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        LookupStatistics? result = service.GetCachedStatistics();

        Assert.IsNotNull( result );
        Assert.AreEqual( 0, result.TotalRecords );
    }

    /// <summary>
    /// Verifies that <c>GetStatisticsAsync</c> with no cache returns empty statistics (zero records,
    /// <see cref="DateTimeOffset.MinValue"/> timestamp) without enumerating any records, confirming
    /// it is a pure read that never triggers computation.
    /// </summary>
    [TestMethod]
    public async Task GetStatisticsAsync_WhenNoCache_ReturnsEmptyStatsWithoutComputation( ) {
        StatisticsService service = CreateService( );

        LookupStatistics result = await service.GetStatisticsAsync(CancellationToken.None);

        Assert.AreEqual( 0, result.TotalRecords );
        Assert.AreEqual( DateTimeOffset.MinValue, result.GeneratedAt );
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never( )
        );
    }

    /// <summary>
    /// Verifies that once a refresh has populated the cache, <c>GetStatisticsAsync</c> returns the
    /// cached data and does not re-enumerate records: the record source is hit only once (from the
    /// refresh), never again from the read.
    /// </summary>
    [TestMethod]
    public async Task GetStatisticsAsync_ReturnsCachedData_NeverTriggersComputation( ) {
        SetupRecordList( [
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC1", "Artist1", "Track1", DateTimeOffset.UtcNow )
        ] );
        StatisticsService service = CreateService( );
        _ = await service.RefreshStatisticsAsync( CancellationToken.None );

        LookupStatistics result = await service.GetStatisticsAsync(CancellationToken.None);

        Assert.AreEqual( 1, result.TotalRecords );
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once( )
        );
    }

    /// <summary>
    /// Verifies that <c>TriggerRefresh</c> returns true when no refresh is currently running,
    /// indicating it started one.
    /// </summary>
    [TestMethod]
    public void TriggerRefresh_WhenNotRefreshing_StartsRefresh( ) {
        StatisticsService service = CreateService( );

        bool result = service.TriggerRefresh();

        Assert.IsTrue( result );
    }

    /// <summary>
    /// Verifies the single-flight guard: while a refresh is in progress (held open by a gated record
    /// stream), <c>TriggerRefresh</c> returns false rather than starting a second concurrent
    /// refresh.
    /// </summary>
    [TestMethod]
    public async Task TriggerRefresh_WhenAlreadyRefreshing_ReturnsFalse( ) {
        TaskCompletionSource<bool> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( BlockingSequence( gate.Task ) );

        StatisticsService service = CreateService( );

        Task<LookupStatistics> refreshTask = service.RefreshStatisticsAsync(CancellationToken.None);

        await WaitUntilAsync( ( ) => service.IsRefreshing, TimeSpan.FromSeconds( 2 ) );
        bool triggerResult = service.TriggerRefresh();

        Assert.IsFalse( triggerResult );
        _ = gate.TrySetResult( true );
        _ = await refreshTask;
    }

    /// <summary>
    /// Verifies that <c>IsRefreshing</c> is false once a refresh has completed, confirming the flag
    /// is cleared on completion.
    /// </summary>
    [TestMethod]
    public async Task IsRefreshing_AfterRefresh_ReturnsFalse( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );

        Assert.IsFalse( service.IsRefreshing );
    }

    /// <summary>
    /// Verifies that a second refresh within the cache-fresh window skips recomputation: two
    /// back-to-back refresh calls enumerate the record source only once.
    /// </summary>
    [TestMethod]
    public async Task RefreshStatisticsAsync_SkipsComputeWhenCacheFresh( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        _ = await service.RefreshStatisticsAsync( CancellationToken.None );

        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once( )
        );
    }

    /// <summary>
    /// Verifies that a forced refresh bypasses the fresh-cache guard: a normal refresh followed by a
    /// forced one enumerates the record source twice.
    /// </summary>
    [TestMethod]
    public async Task RefreshStatisticsAsync_ForceRefresh_BypassesFreshCache( ) {
        SetupEmptyRecordList( );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        _ = await service.RefreshStatisticsAsync( true, CancellationToken.None );

        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 )
        );
    }

    /// <summary>
    /// Verifies that a refresh over one track record and one album record updates the cache with the
    /// correct totals: two records overall, classified as one album and one track.
    /// </summary>
    [TestMethod]
    public async Task RefreshStatisticsAsync_UpdatesCachedStats( ) {
        SetupRecordList( [
            CreateTrackRecord( "at://did:plc:test/link.bridgebeats.lookup/track:ISRC1", "Artist1", "Track1", DateTimeOffset.UtcNow ),
            CreateAlbumRecord( "at://did:plc:test/link.bridgebeats.lookup/album:UPC1", "Artist2", "Album1", DateTimeOffset.UtcNow )
        ] );
        StatisticsService service = CreateService( );

        _ = await service.RefreshStatisticsAsync( CancellationToken.None );
        LookupStatistics? cached = service.GetCachedStatistics();

        Assert.IsNotNull( cached );
        Assert.AreEqual( 2, cached.TotalRecords );
        Assert.AreEqual( 1, cached.AlbumCount );
        Assert.AreEqual( 1, cached.TrackCount );
    }

    /// <summary>
    /// Builds a service wired to the storage, Redis, settings, and logger fixtures.
    /// </summary>
    /// <returns>A service under test.</returns>
    private StatisticsService CreateService( ) {
        return new StatisticsService(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            _settings,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Configures the storage mock to return an empty record stream, so a refresh produces
    /// zero-count statistics.
    /// </summary>
    private void SetupEmptyRecordList( ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );
    }

    /// <summary>
    /// Configures the storage mock to return the supplied records as the enumerable stream a refresh
    /// aggregates.
    /// </summary>
    /// <param name="records">The AT-URI / result pairs to surface during enumeration.</param>
    private void SetupRecordList( List<(string AtUri, MediaLinkResult Result)> records ) {
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( records.ToAsyncEnumerable( ) );
    }

    /// <summary>
    /// An async record stream that blocks until <paramref name="releaseTask"/> completes, then
    /// yields nothing. Used to hold a refresh open while the single-flight guard is exercised.
    /// </summary>
    /// <param name="releaseTask">The task whose completion releases the stream.</param>
    /// <returns>An empty stream that completes only after the release task.</returns>
    private static async IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> BlockingSequence( Task releaseTask ) {
        await releaseTask;
        yield break;
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses,
    /// failing the test on timeout. Used to await asynchronous refresh state transitions.
    /// </summary>
    /// <param name="condition">The predicate to wait for.</param>
    /// <param name="timeout">The maximum time to wait before failing.</param>
    private static async Task WaitUntilAsync( Func<bool> condition, TimeSpan timeout ) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            if (condition( )) {
                return;
            }

            await Task.Delay( 10 );
        }

        Assert.Fail( "Condition not met before timeout." );
    }

    /// <summary>
    /// Builds a track record (a single Spotify result with <c>IsAlbum</c> false) wrapped with the
    /// given AT URI and lookup timestamp, for refresh aggregation tests.
    /// </summary>
    /// <param name="atUri">The record's AT URI.</param>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The track title.</param>
    /// <param name="lookedUpAt">The lookup timestamp recorded on the result.</param>
    /// <returns>An AT-URI / result pair representing a track.</returns>
    private static (string AtUri, MediaLinkResult Result) CreateTrackRecord(
        string atUri,
        string artist,
        string title,
        DateTimeOffset lookedUpAt
    ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = lookedUpAt.UtcDateTime
        };
        result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
            Artist = artist,
            Title = title,
            IsAlbum = false,
            ExternalId = "ISRC12345678",
            URL = "https://open.spotify.com/track/test"
        } );
        return (atUri, result);
    }

    /// <summary>
    /// Builds an album record (a single Spotify result with <c>IsAlbum</c> true) wrapped with the
    /// given AT URI and lookup timestamp, for refresh aggregation tests.
    /// </summary>
    /// <param name="atUri">The record's AT URI.</param>
    /// <param name="artist">The artist name.</param>
    /// <param name="title">The album title.</param>
    /// <param name="lookedUpAt">The lookup timestamp recorded on the result.</param>
    /// <returns>An AT-URI / result pair representing an album.</returns>
    private static (string AtUri, MediaLinkResult Result) CreateAlbumRecord(
        string atUri,
        string artist,
        string title,
        DateTimeOffset lookedUpAt
    ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = lookedUpAt.UtcDateTime
        };
        result.Results.Add( SupportedProviders.Spotify, new MusicLookupResult {
            Artist = artist,
            Title = title,
            IsAlbum = true,
            ExternalId = "123456789012",
            URL = "https://open.spotify.com/album/test"
        } );
        return (atUri, result);
    }

    /// <summary>
    /// Verifies that two successive periodic refreshes (with a non-trivial simulated compute
    /// duration) both proceed without the second being skipped. This test is designed to
    /// discriminate between the fixed START-anchored expiry and the old COMPLETION-anchored expiry.
    ///
    /// Timing (all relative to the first refresh START):
    /// <list type="bullet">
    ///   <item>t=0ms: first refresh starts; <c>refreshStart</c> is captured.</item>
    ///   <item>t≈100ms: first refresh completes (simulated 100 ms compute delay).</item>
    ///   <item>Fixed code: <c>_cacheExpiry = refreshStart + 150ms = t150</c>.</item>
    ///   <item>Old (buggy) code: <c>_cacheExpiry = completion + 150ms ≈ t250</c>.</item>
    ///   <item>t≈160ms: second refresh fires (60 ms post-completion wait).</item>
    ///   <item>t160 &gt; t150 (fixed expiry) → fixed code recomputes (correct).</item>
    ///   <item>t160 &lt; t250 (old expiry) → old code would still see cache as fresh and skip (bug).</item>
    /// </list>
    ///
    /// If the production code were reverted to completion-anchored expiry, the second refresh at
    /// t≈160 ms would find <c>now &lt; _cacheExpiry</c> (160 &lt; 250) and skip, making
    /// <c>countAfterSecond == 1</c> rather than 2, causing the assertion below to fail.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RefreshStatisticsAsync_TwoSuccessivePeriodicRefreshes_NeitherSkipped( ) {
        // Arrange
        // CacheDuration = 150ms, ComputeDelay = 100ms, PostCompleteWait = 60ms.
        // This places the second call at ~t160ms, inside the discriminating window (t150, t250)
        // where fixed and old code diverge: fixed sees an expired cache (recomputes), old sees a
        // fresh cache (skips).
        const int CacheDurationMs = 150;
        const int ComputeDelayMs = 100;
        const int PostCompleteWaitMs = 60;

        StatisticsSettings fastSettings = new(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromMilliseconds( CacheDurationMs ),
            TimeSpan.Zero
        );

        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
            .Returns( SimulatedSlowEnumerable );

        StatisticsService service = new(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            fastSettings,
            _loggerMock.Object
        );

        // Act: first refresh (non-forced, simulates periodic tick 1)
        _ = await service.RefreshStatisticsAsync( false, CancellationToken.None );
        int countAfterFirst = _atProtoStorageMock.Invocations.Count( i =>
            i.Method.Name == nameof( IATProtoStorageService.ListAllRecordsAsync ) );

        // Wait PostCompleteWaitMs after completion (~t160ms from start) — inside the
        // discriminating window (t150ms fixed expiry, t250ms old completion-anchored expiry).
        await Task.Delay( PostCompleteWaitMs, TestContext.CancellationToken );

        // Second refresh (non-forced, simulates periodic tick 2)
        _ = await service.RefreshStatisticsAsync( false, CancellationToken.None );
        int countAfterSecond = _atProtoStorageMock.Invocations.Count( i =>
            i.Method.Name == nameof( IATProtoStorageService.ListAllRecordsAsync ) );

        // Assert: both ticks must have resulted in a recompute; the second must not be skipped.
        Assert.AreEqual( 1, countAfterFirst,
            "First periodic refresh must enumerate records (not skipped)" );
        Assert.AreEqual( 2, countAfterSecond,
            "Second periodic refresh must enumerate records; completion-anchored expiry would still show cache as fresh (~t250ms) at the second call (~t160ms) and skip it" );

        return;

        async IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> SimulatedSlowEnumerable( ) {
            await Task.Delay( ComputeDelayMs, CancellationToken.None );
            yield break;
        }
    }

    /// <summary>
    /// Verifies that a forced refresh (manual trigger) bypasses the fresh-cache skip guard even
    /// when the computed expiry would still be in the future, confirming that the start-anchored
    /// expiry fix preserves the manual-coalescing behavior.
    /// </summary>
    [TestMethod]
    public async Task RefreshStatisticsAsync_ForcedRefreshWhileFresh_AlwaysRecomputes( ) {
        // Arrange: use a very long cache duration so the cache stays fresh
        StatisticsSettings longCacheSettings = new(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 24 ),
            TimeSpan.Zero
        );

        SetupEmptyRecordList( );
        StatisticsService service = new(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            longCacheSettings,
            _loggerMock.Object
        );

        // First refresh populates cache
        _ = await service.RefreshStatisticsAsync( false, CancellationToken.None );

        // Force refresh while still fresh
        _ = await service.RefreshStatisticsAsync( true, CancellationToken.None );

        // Assert: both calls enumerate records — forced bypass is preserved
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Exactly( 2 ) );
    }

    /// <summary>
    /// Verifies that when the cache has not yet expired, a non-forced refresh is skipped as
    /// intended (the manual-coalescing guard remains intact after the expiry-anchor fix).
    /// </summary>
    [TestMethod]
    public async Task RefreshStatisticsAsync_NonForcedRefreshWhileFresh_IsSkipped( ) {
        // Arrange: long cache duration so the second non-forced call sees a fresh cache
        StatisticsSettings longCacheSettings = new(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromHours( 24 ),
            TimeSpan.Zero
        );

        SetupEmptyRecordList( );
        StatisticsService service = new(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            longCacheSettings,
            _loggerMock.Object
        );

        _ = await service.RefreshStatisticsAsync( false, CancellationToken.None );
        _ = await service.RefreshStatisticsAsync( false, CancellationToken.None );

        // Assert: second non-forced call inside the cache window is skipped — only one enumerate
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Once );
    }

    /// <summary>
    /// Verifies that when Redis holds a serialized bootstrap status at the well-known key,
    /// <c>GetLiveBootstrapStatusAsync</c> deserializes and returns it (running flag, success count,
    /// duration), and does so without enumerating any records.
    /// </summary>
    [TestMethod]
    public async Task GetLiveBootstrapStatusAsync_WhenRedisHasStatus_ReturnsDeserializedStatus( ) {
        // Arrange: pre-load a completed status into the Redis mock
        CacheBootstrapStatus expected = new( ) {
            IsRunning = false,
            LastRunTime = DateTimeOffset.UtcNow.AddHours( -2 ),
            LastSuccessCount = 247404,
            LastErrorCount = 0,
            LastDurationSeconds = 4565.9,
            RedisKeyCount = 2882803
        };
        string json = System.Text.Json.JsonSerializer.Serialize( expected );
        _ = _redisDatabaseMock
            .Setup( x => x.StringGetAsync( CacheBootstrapStatus.RedisKey, It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisValue)json );

        StatisticsService service = CreateService( );

        // Act
        CacheBootstrapStatus? result = await service.GetLiveBootstrapStatusAsync( CancellationToken.None );

        // Assert: returns the deserialized status directly from Redis (not cached stats)
        Assert.IsNotNull( result );
        Assert.IsFalse( result.IsRunning );
        Assert.AreEqual( 247404, result.LastSuccessCount );
        Assert.AreEqual( 4565.9, result.LastDurationSeconds );

        // Verify that ATProto storage was NOT called (live status does not trigger a refresh)
        _atProtoStorageMock.Verify(
            x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ),
            Times.Never( )
        );
    }

    /// <summary>
    /// Verifies that when Redis has no bootstrap status stored, <c>GetLiveBootstrapStatusAsync</c>
    /// returns null.
    /// </summary>
    [TestMethod]
    public async Task GetLiveBootstrapStatusAsync_WhenRedisReturnsNull_ReturnsNull( ) {
        // Default mock already returns RedisValue.Null
        StatisticsService service = CreateService( );

        CacheBootstrapStatus? result = await service.GetLiveBootstrapStatusAsync( CancellationToken.None );

        Assert.IsNull( result );
    }

    /// <summary>
    /// Verifies that when the Redis read throws (for example, a connection failure),
    /// <c>GetLiveBootstrapStatusAsync</c> swallows the exception and returns null rather than
    /// propagating, so a Redis outage degrades gracefully.
    /// </summary>
    [TestMethod]
    public async Task GetLiveBootstrapStatusAsync_WhenRedisThrows_ReturnsNullWithoutThrowing( ) {
        _ = _redisDatabaseMock
            .Setup( x => x.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ThrowsAsync( new StackExchange.Redis.RedisException( "Connection refused" ) );

        StatisticsService service = CreateService( );

        // Act: should not throw; returns null gracefully
        CacheBootstrapStatus? result = await service.GetLiveBootstrapStatusAsync( CancellationToken.None );

        Assert.IsNull( result );
    }
}

#pragma warning restore CS1591
