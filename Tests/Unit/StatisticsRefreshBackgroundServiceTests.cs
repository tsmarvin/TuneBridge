using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StatisticsRefreshBackgroundService"/>, the hosted service that drives
/// periodic and manually triggered statistics refreshes against a real
/// <see cref="StatisticsService"/>. Verifies that periodic ticks plus manual triggers produce at
/// least the expected number of refreshes with no logged errors, that two consecutive manual
/// triggers are handled cleanly, and that when refreshes throw, a fixed-delay backoff caps the error
/// rate while the service loop keeps running (its execute task stays alive and unfaulted).
/// </summary>
[TestClass]
public class StatisticsRefreshBackgroundServiceTests {

    /// <summary>Mock storage service backing the statistics service the background loop refreshes.</summary>
    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    /// <summary>Mock Redis multiplexer providing the database used for bootstrap-status reads.</summary>
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    /// <summary>Mock Redis database backing bootstrap-status reads.</summary>
    private Mock<IDatabase> _redisDatabaseMock = null!;
    /// <summary>
    /// Statistics settings using a short (100 ms) refresh interval and zero cache TTL so periodic
    /// ticks and forced recomputation happen quickly within test time budgets.
    /// </summary>
    private StatisticsSettings _settings = null!;

    /// <summary>Test PDS URI the statistics service enumerates records from.</summary>
    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    /// <summary>Test user DID whose repository is enumerated.</summary>
    private const string TestUserDid = "did:plc:testuser_bgservice";

    /// <summary>
    /// MSTest-injected context; its cancellation token bounds the in-test delays so a hung loop does
    /// not outlive the test.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Creates fresh mocks before each test, defaults Redis and record enumeration to empty, and
    /// configures fast-tick settings (100 ms interval, zero TTL).
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _atProtoStorageMock = new Mock<IATProtoStorageService>( );
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _redisDatabaseMock = new Mock<IDatabase>( );
        _ = _redisMock.Setup( x => x.GetDatabase( It.IsAny<int>( ), It.IsAny<object>( ) ) )
            .Returns( _redisDatabaseMock.Object );
        _ = _redisDatabaseMock.Setup( x => x.StringGetAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( RedisValue.Null );

        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Returns( AsyncEnumerable.Empty<(string, MediaLinkResult)>( ) );

        // Short CacheDuration so the periodic timer fires during tests; zero StartupDelay.
        _settings = new StatisticsSettings(
            s_testPdsUri,
            TestUserDid,
            TimeSpan.FromMilliseconds( 100 ),
            TimeSpan.Zero
        );
    }

    /// <summary>
    /// Verifies that the background loop combined with a manual trigger drives at least four
    /// refreshes (initial periodic tick, the manual trigger, and subsequent ticks) with zero errors
    /// logged, confirming periodic and on-demand refresh coexist cleanly.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ManualTriggerFollowedByPeriodicTick_NoErrors_AtLeastFourRefreshes( ) {
        // Arrange
        int refreshCount = 0;
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Returns( ( ) => {
                _ = Interlocked.Increment( ref refreshCount );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        Mock<ILogger<StatisticsRefreshBackgroundService>> loggerMock = new( );
        int errorCount = 0;
        _ = loggerMock
            .Setup( l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ) )
            .Callback( ( ) => _ = Interlocked.Increment( ref errorCount ) );

        StatisticsService statisticsService = new(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            _settings,
            Mock.Of<ILogger<StatisticsService>>( )
        );
        StatisticsRefreshBackgroundService backgroundService = new(
            statisticsService,
            _settings,
            loggerMock.Object
        );

        using CancellationTokenSource cts = new( );

        // Act: start service, trigger a manual refresh, then wait for the periodic tick to fire
        _ = backgroundService.StartAsync( cts.Token );

        // Wait for startup initial refresh
        await WaitUntilAsync( ( ) => Volatile.Read( ref refreshCount ) >= 1, TimeSpan.FromSeconds( 5 ) );

        // Fire a manual trigger — this is what triggered the hot loop bug.
        // Use WaitUntilAsync so a silently-rejected trigger cannot make the test pass on
        // periodic ticks alone (TriggerRefresh returns false while a refresh is in flight).
        await WaitUntilAsync( statisticsService.TriggerRefresh, TimeSpan.FromSeconds( 5 ) );

        // Wait until >=4 refreshes: startup(1) + manual(2) + two periodic ticks(3,4).
        // Reaching 4 proves the timer waiter is re-armed and keeps firing after a manual
        // trigger — the property the hoist/re-arm fix must preserve. Bounded polling, no flake.
        await WaitUntilAsync( ( ) => Volatile.Read( ref refreshCount ) >= 4, TimeSpan.FromSeconds( 5 ) );

        await cts.CancelAsync( );
        await backgroundService.StopAsync( CancellationToken.None );

        // Assert — lowerBound=4; the actual count must be >= 4
        Assert.IsGreaterThanOrEqualTo( 4, Volatile.Read( ref refreshCount ), $"Expected at least 4 refreshes, got {refreshCount}" );
        Assert.AreEqual( 0, Volatile.Read( ref errorCount ), $"Expected 0 errors, got {errorCount}" );
    }

    /// <summary>
    /// Verifies that two manual triggers issued back to back are handled without logging any errors,
    /// confirming overlapping or rapid triggers do not destabilize the refresh loop.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TwoConsecutiveManualTriggers_NoErrors( ) {
        // Arrange
        int refreshCount = 0;
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Returns( ( ) => {
                _ = Interlocked.Increment( ref refreshCount );
                return AsyncEnumerable.Empty<(string, MediaLinkResult)>( );
            } );

        Mock<ILogger<StatisticsRefreshBackgroundService>> loggerMock = new( );
        int errorCount = 0;
        _ = loggerMock
            .Setup( l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ) )
            .Callback( ( ) => _ = Interlocked.Increment( ref errorCount ) );

        StatisticsService statisticsService = new(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            _settings,
            Mock.Of<ILogger<StatisticsService>>( )
        );
        StatisticsRefreshBackgroundService backgroundService = new(
            statisticsService,
            _settings,
            loggerMock.Object
        );

        using CancellationTokenSource cts = new( );

        // Act
        _ = backgroundService.StartAsync( cts.Token );

        // Wait for the startup refresh before firing manual triggers
        await WaitUntilAsync( ( ) => Volatile.Read( ref refreshCount ) >= 1, TimeSpan.FromSeconds( 5 ) );

        // Retry each trigger until the channel accepts it: TriggerRefresh returns false while a
        // refresh is in flight (IsRefreshing guard), and refreshCount increments at refresh START,
        // so a fixed gap flakes on a starved runner.
        await WaitUntilAsync( statisticsService.TriggerRefresh, TimeSpan.FromSeconds( 5 ) );
        await WaitUntilAsync( statisticsService.TriggerRefresh, TimeSpan.FromSeconds( 5 ) );

        // Per-trigger refresh attribution is NOT assertable: the channel is Bounded(1)/DropNewest
        // and the loop drains pending signals, so accepted triggers may coalesce; periodic ticks
        // (100 ms) also increment the count. Properties under test: both triggers accepted
        // (WaitUntilAsync fails otherwise), zero errors, and the loop keeps refreshing.
        int countAfterTriggers = Volatile.Read( ref refreshCount );
        await WaitUntilAsync( ( ) => Volatile.Read( ref refreshCount ) > countAfterTriggers, TimeSpan.FromSeconds( 5 ) );

        await cts.CancelAsync( );
        await backgroundService.StopAsync( CancellationToken.None );

        Assert.AreEqual( 0, Volatile.Read( ref errorCount ), $"Expected 0 errors after two consecutive triggers, got {errorCount}" );
    }

    /// <summary>
    /// Verifies that when every refresh throws, the fixed-delay catch-backoff limits the error rate
    /// to at most three logged errors within a one-second observation window, and the service loop
    /// keeps running: its execute task is non-null, not completed during the window, and not faulted
    /// after a clean stop.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RefreshThatThrows_BackoffLimitsErrors_ServiceKeepsRunning( ) {
        // Arrange — ALL ListAllRecordsAsync calls throw so both the startup refresh and every
        // loop-phase iteration enter their respective catch branches.
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ), It.IsAny<bool>( ) ) )
            .Returns( ThrowingEnumerable );

        Mock<ILogger<StatisticsRefreshBackgroundService>> loggerMock = new( );
        int errorCount = 0;
        _ = loggerMock
            .Setup( l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>( ),
                It.IsAny<It.IsAnyType>( ),
                It.IsAny<Exception>( ),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>( ) ) )
            .Callback( ( ) => _ = Interlocked.Increment( ref errorCount ) );

        StatisticsService statisticsService = new(
            _atProtoStorageMock.Object,
            _redisMock.Object,
            _settings,
            Mock.Of<ILogger<StatisticsService>>( )
        );
        StatisticsRefreshBackgroundService backgroundService = new(
            statisticsService,
            _settings,
            loggerMock.Object
        );

        using CancellationTokenSource cts = new( );

        // Act — observe for ~1 s; CacheDuration is 100 ms so without the 5 s backoff the loop
        // would accumulate ~10 errors. With the backoff, at most 3 errors can fire in 1 s.
        _ = backgroundService.StartAsync( cts.Token );

        // Wait for the startup refresh to throw at least once
        await WaitUntilAsync( ( ) => Volatile.Read( ref errorCount ) >= 1, TimeSpan.FromSeconds( 5 ) );

        // Observe for 1 s; the loop-phase backoff must suppress subsequent errors
        await Task.Delay( 1000, TestContext.CancellationToken );
        int errorsAfterObservationWindow = Volatile.Read( ref errorCount );

        // Liveness: ExecuteTask is the real ExecuteAsync task (serviceTask from StartAsync
        // completes at the first await; its IsFaulted is vacuously false).
        Assert.IsNotNull( backgroundService.ExecuteTask, "ExecuteTask must exist after StartAsync" );
        Assert.IsFalse( backgroundService.ExecuteTask.IsCompleted, "Service loop must still be running after errors (backoff in progress)" );

        await cts.CancelAsync( );
        await backgroundService.StopAsync( CancellationToken.None );

        // Assert: at most 3 errors in the observation window (startup + loop startup iteration).
        // Without the backoff this reaches 8–10. The discriminator is tight enough to catch removal.
        Assert.IsLessThanOrEqualTo( 3, errorsAfterObservationWindow,
            $"Expected at most 3 errors in 1 s window (backoff is active); got {errorsAfterObservationWindow}" );

        // Service loop must not have faulted (ExecuteTask is the real long-running task).
        Assert.IsFalse( backgroundService.ExecuteTask.IsFaulted, "ExecuteAsync must not be faulted after StopAsync" );
    }

    /// <summary>
    /// An async record stream that always throws <see cref="InvalidOperationException"/> when
    /// enumerated, simulating a refresh that fails so the backoff and resilience behavior can be
    /// observed.
    /// </summary>
    /// <returns>A stream that never yields and always throws on enumeration.</returns>
    private static async IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> ThrowingEnumerable( ) {
        await Task.Yield( );
        throw new InvalidOperationException( "Simulated refresh failure" );
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses,
    /// failing the test on timeout. Used to await refresh-count and error-count thresholds reached
    /// asynchronously by the background loop.
    /// </summary>
    /// <param name="condition">The predicate to wait for.</param>
    /// <param name="timeout">The maximum time to wait before failing.</param>
    private static async Task WaitUntilAsync( Func<bool> condition, TimeSpan timeout ) {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline) {
            if (condition( )) {
                return;
            }
            await Task.Delay( 20 );
        }
        Assert.Fail( "Condition not met before timeout." );
    }
}

#pragma warning restore CS1591
