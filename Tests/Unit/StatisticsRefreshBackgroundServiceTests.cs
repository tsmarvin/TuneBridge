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
/// Unit tests for <see cref="StatisticsRefreshBackgroundService"/> verifying the PeriodicTimer
/// hot-loop regression is fixed: a manual trigger must not leave an orphaned WaitForNextTickAsync
/// waiter that causes InvalidOperationException on the next iteration.
/// </summary>
[TestClass]
public class StatisticsRefreshBackgroundServiceTests {

    private Mock<IATProtoStorageService> _atProtoStorageMock = null!;
    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _redisDatabaseMock = null!;
    private StatisticsSettings _settings = null!;

    private static readonly Uri s_testPdsUri = new( "https://pds.test.example" );
    private const string TestUserDid = "did:plc:testuser_bgservice";

    /// <summary>
    /// Gets or sets the test context for cooperative cancellation support.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

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
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
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
    /// Regression test: a manual TriggerRefresh followed by periodic ticks must produce at least
    /// four refreshes and zero LogRefreshError invocations. Requiring >= 4 (startup + manual + two
    /// periodic ticks) verifies the timer waiter is re-armed and keeps firing after a manual trigger —
    /// the property the hoist/re-arm fix must preserve. On the pre-fix code shape, the second
    /// WaitForNextTickAsync call inside the loop throws InvalidOperationException immediately after
    /// the channel task wins WhenAny.
    ///
    /// Failure-first evidence: verified on the original StatisticsRefreshBackgroundService.cs (tasks
    /// created inside the while loop). The service threw InvalidOperationException within milliseconds
    /// of TriggerRefresh being called — refreshCount did reach 2 (startup + trigger) but errorCount
    /// rose continuously. The discriminating assertion is errorCount == 0: on the pre-fix code, it
    /// fails immediately with errorCount > 0.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task ManualTriggerFollowedByPeriodicTick_NoErrors_AtLeastFourRefreshes( ) {
        // Arrange
        int refreshCount = 0;
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
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
        Task serviceTask = backgroundService.StartAsync( cts.Token );

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
    /// Two consecutive manual triggers must both be accepted and must not produce errors. After both
    /// triggers are accepted the service loop must keep refreshing (at least one more refresh fires).
    ///
    /// Per-trigger refresh attribution is NOT assertable: the channel is Bounded(1)/DropNewest and
    /// the loop drains pending signals, so accepted triggers may coalesce; periodic ticks (100 ms)
    /// also increment the count. Properties under test: both triggers accepted (WaitUntilAsync fails
    /// otherwise), zero errors, and the loop keeps refreshing.
    ///
    /// Failure-first evidence: on the pre-fix code, the first TriggerRefresh caused channelTask to
    /// win WhenAny. The next loop iteration called WaitForNextTickAsync a second time while the first
    /// was still registered, producing InvalidOperationException. errorCount > 0.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task TwoConsecutiveManualTriggers_NoErrors( ) {
        // Arrange
        int refreshCount = 0;
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
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
        Task serviceTask = backgroundService.StartAsync( cts.Token );

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
    /// When ALL refreshes throw (startup and every loop iteration), the 5 s catch-backoff limits
    /// errors to at most 3 over a 1 s observation window. Without the backoff the loop would spin
    /// at the 100 ms CacheDuration producing ~10 errors/s. The service must remain alive.
    ///
    /// Discriminator: removing the Task.Delay(5 s) from the loop catch causes errorCount to exceed 3
    /// well within the 1 s window (each periodic tick is 100 ms, so 8–10 errors accumulate).
    ///
    /// Failure-first evidence: verified by removing Task.Delay(TimeSpan.FromSeconds(5), stoppingToken)
    /// from the catch(Exception) in StatisticsRefreshBackgroundService.ExecuteAsync — errorCount
    /// reached 6 within 600 ms. With the backoff in place, at most 2 errors fire (startup + first
    /// loop iteration before the 5 s delay kicks in).
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task RefreshThatThrows_BackoffLimitsErrors_ServiceKeepsRunning( ) {
        // Arrange — ALL ListAllRecordsAsync calls throw so both the startup refresh and every
        // loop-phase iteration enter their respective catch branches.
        _ = _atProtoStorageMock
            .Setup( x => x.ListAllRecordsAsync( It.IsAny<Uri>( ), It.IsAny<string>( ), It.IsAny<CancellationToken>( ) ) )
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

    private static async IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> ThrowingEnumerable( ) {
        await Task.Yield( );
        throw new InvalidOperationException( "Simulated refresh failure" );
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

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
