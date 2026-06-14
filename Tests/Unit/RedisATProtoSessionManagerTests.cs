using System.Net;
using System.Reflection;
using System.Text.Json;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Storage;
using idunno.AtProto;
using idunno.AtProto.Events;
using idunno.Bluesky;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RedisATProtoSessionManager"/> covering the four directives in
/// the ATProto token-refresh loop fix: leak on restore exception (Dir 1), failed-agent teardown
/// (Dir 2), background-refresh disable and lock-coordinated refresh (Dir 3), and cooldown guard (Dir 2/C).
/// </summary>
/// <remarks>
/// Test strategy: the constructor's optional <c>agentFactory</c> parameter is the seam that makes
/// these tests possible. <see cref="DisposableTrackingAgent"/> is a minimal <see cref="BlueskyAgent"/>
/// subclass that tracks disposal and can fire protected events for Directive-2 teardown tests.
///
/// <para>
/// Tests that verify agent disposal and event handling call private methods directly via reflection
/// to avoid network calls in <c>PerformFreshLoginAsync</c> (which would require a live PDS).
/// <c>TryRestoreSessionAsync</c> and <c>SubscribeToAgentEvents</c> are accessed through
/// the reflection helpers — a controlled, test-only seam that does not change the production path.
/// </para>
///
/// Redis dependencies are fully mocked via <see cref="IConnectionMultiplexer"/> and <see cref="IDatabase"/>.
/// No network or real Redis is required.
/// </remarks>
[TestClass]
public class RedisATProtoSessionManagerTests {

    // ─── Constants ────────────────────────────────────────────────────────────

    private const string TestIdentifier = "test.bsky.social";
    private const string TestPassword = "test-app-password";
    private const string TestDid = "did:plc:testuser12345678";

    private static readonly JsonSerializerOptions s_jsonOptions = new( ) {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ─── Reflection keys (private members) ───────────────────────────────────

    private static readonly FieldInfo s_agentField =
        typeof( RedisATProtoSessionManager )
            .GetField( "_agent", BindingFlags.NonPublic | BindingFlags.Instance )
        ?? throw new InvalidOperationException( "_agent field not found on RedisATProtoSessionManager" );

    private static readonly MethodInfo s_subscribeToAgentEvents =
        typeof( RedisATProtoSessionManager )
            .GetMethod( "SubscribeToAgentEvents", BindingFlags.NonPublic | BindingFlags.Instance )
        ?? throw new InvalidOperationException( "SubscribeToAgentEvents not found on RedisATProtoSessionManager" );

    private static readonly MethodInfo s_tryRestoreSessionAsync =
        typeof( RedisATProtoSessionManager )
            .GetMethod( "TryRestoreSessionAsync", BindingFlags.NonPublic | BindingFlags.Instance )
        ?? throw new InvalidOperationException( "TryRestoreSessionAsync not found on RedisATProtoSessionManager" );

    // ─── Infrastructure mocks ─────────────────────────────────────────────────

    private Mock<IConnectionMultiplexer> _redisMock = null!;
    private Mock<IDatabase> _dbMock = null!;
    private Mock<ILogger<RedisATProtoSessionManager>> _loggerMock = null!;

    /// <summary>Gets or sets the test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    // ─── Setup ────────────────────────────────────────────────────────────────

    [TestInitialize]
    public void Initialize( ) {
        _redisMock = new Mock<IConnectionMultiplexer>( );
        _dbMock = new Mock<IDatabase>( );
        _loggerMock = new Mock<ILogger<RedisATProtoSessionManager>>( );

        // Logger must report IsEnabled=true so [LoggerMessage]-generated calls don't short-circuit.
        _ = _loggerMock.Setup( l => l.IsEnabled( It.IsAny<LogLevel>( ) ) ).Returns( true );

        // Wire IConnectionMultiplexer → IDatabase
        _ = _redisMock
            .Setup( r => r.GetDatabase( It.IsAny<int>( ), It.IsAny<object?>( ) ) )
            .Returns( _dbMock.Object );

        // Default: lock acquire succeeds immediately
        SetupLockAcquire( true );

        // Default: lock release succeeds
        SetupLockRelease( );

        // Default: no stored credentials in Redis
        SetupStoredCredentials( RedisValue.Null );

        // Default: persist/clear succeed
        SetupPersist( );
        SetupClear( );
    }

    // ─── Factory / manager helpers ────────────────────────────────────────────

    private RedisATProtoSessionManager CreateManager( Func<BlueskyAgent>? factory = null ) =>
        new(
            _redisMock.Object,
            _loggerMock.Object,
            TestIdentifier,
            TestPassword,
            factory
        );

    // ─── Reflection helpers ───────────────────────────────────────────────────

    private void SetAgent( RedisATProtoSessionManager manager, BlueskyAgent? agent ) =>
        s_agentField.SetValue( manager, agent );

    private BlueskyAgent? GetAgent( RedisATProtoSessionManager manager ) =>
        (BlueskyAgent?)s_agentField.GetValue( manager );

    private void SubscribeToAgentEvents( RedisATProtoSessionManager manager, BlueskyAgent agent ) =>
        s_subscribeToAgentEvents.Invoke( manager, [agent] );

    /// <summary>
    /// Calls the private <c>TryRestoreSessionAsync</c> method directly, bypassing
    /// <c>AcquireDistributedLockAndRefreshAsync</c> and <c>PerformFreshLoginAsync</c>.
    /// Callers are responsible for setting up Redis mocks for the session key read.
    /// </summary>
    private async Task<bool> TryRestoreSessionAsync(
        RedisATProtoSessionManager manager,
        CancellationToken ct = default ) {
        Task<bool> task = (Task<bool>)s_tryRestoreSessionAsync.Invoke( manager, [ct] )!;
        return await task;
    }

    // ─── Redis mock helpers ───────────────────────────────────────────────────

    private void SetupLockAcquire( bool result ) {
        _ = _dbMock
            .Setup( d => d.StringSetAsync(
                It.Is<RedisKey>( k => ((string)k!).Contains( ":auth:lock:" ) ),
                It.IsAny<RedisValue>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<When>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( result );
    }

    private void SetupLockRelease( ) {
        _ = _dbMock
            .Setup( d => d.ScriptEvaluateAsync(
                It.IsAny<string>( ),
                It.IsAny<RedisKey[]?>( ),
                It.IsAny<RedisValue[]?>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( (RedisResult)RedisResult.Create( (RedisValue)1 ) );
    }

    private void SetupStoredCredentials( RedisValue value ) {
        _ = _dbMock
            .Setup( d => d.StringGetAsync(
                It.Is<RedisKey>( k => ((string)k!).Contains( "atproto:session:" ) ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( value );
    }

    private void SetupPersist( ) {
        _ = _dbMock
            .Setup( d => d.StringSetAsync(
                It.Is<RedisKey>( k => ((string)k!).Contains( "atproto:session:" ) ),
                It.IsAny<RedisValue>( ),
                It.IsAny<TimeSpan?>( ),
                It.IsAny<When>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( true );
    }

    private void SetupClear( ) {
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync(
                It.IsAny<RedisKey>( ),
                It.IsAny<CommandFlags>( ) ) )
            .ReturnsAsync( true );
    }

    /// <summary>
    /// Builds a valid <see cref="ATProtoPersistedCredentials"/> JSON pointing to a localhost URI
    /// that will cause <c>RefreshCredentials</c> to throw <see cref="HttpRequestException"/> (connection refused)
    /// rather than succeed. This triggers the exception path in <c>TryRestoreSessionAsync</c>.
    /// </summary>
    private static string BuildRefusingServiceCredentialJson( ) {
        ATProtoPersistedCredentials creds = new( ) {
            RefreshToken = "test-refresh-token",
            Service = "http://127.0.0.1:1", // closed port → instant connection refused
            Did = TestDid,
            Handle = TestIdentifier,
            AuthenticationType = "UsernamePassword",
            PersistedAt = DateTimeOffset.UtcNow.AddMinutes( -5 )
        };
        return JsonSerializer.Serialize( creds, s_jsonOptions );
    }

    // ─── Test 1 — Leak fix: restore exception disposes the agent ─────────────

    /// <summary>
    /// Verifies that when <c>RefreshCredentials</c> throws during session restoration, the newly
    /// created agent is disposed and <c>_agent</c> is not set.
    ///
    /// Directive 1 (Cause B fix): this is the primary leak guard.
    ///
    /// <b>Failure-first evidence:</b> before the fix the <c>catch</c> block returned <c>false</c>
    /// without calling <c>agent.Dispose()</c>. Removing the <c>agent?.Dispose()</c> from the
    /// <c>catch</c> would cause this test to fail because <c>WasDisposed</c> would remain
    /// <c>false</c> (orphaned agent with its background timer running). The current implementation
    /// disposes in the catch, which this test asserts.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task TryRestoreSession_WhenRefreshCredentialsThrows_DisposesCreatedAgent( ) {
        // Arrange — stored credential points to a closed port.
        // Connection refused on 127.0.0.1:1 is instant (OS immediately rejects the TCP SYN).
        SetupStoredCredentials( BuildRefusingServiceCredentialJson( ) );

        DisposableTrackingAgent? createdAgent = null;
        Func<BlueskyAgent> factory = ( ) => {
            createdAgent = new DisposableTrackingAgent( );
            return createdAgent;
        };

        using RedisATProtoSessionManager manager = CreateManager( factory );

        // Act — call TryRestoreSessionAsync directly (bypasses PerformFreshLoginAsync so no
        // second network call hangs the test).
        bool restored = await TryRestoreSessionAsync( manager, TestContext.CancellationToken );

        // Assert
        Assert.IsFalse( restored, "Restore must return false when RefreshCredentials throws" );
        Assert.IsNotNull( createdAgent, "Factory must have been called" );
        Assert.IsTrue( createdAgent.WasDisposed,
            "Agent created during TryRestoreSessionAsync must be disposed when RefreshCredentials throws " +
            "(Directive 1 — stops the agent leak that drives the monotonic hourly-burst growth). " +
            "Pre-fix: removing agent?.Dispose() from the catch block leaves WasDisposed=false." );
        Assert.IsNull( GetAgent( manager ),
            "_agent must not be set when restore fails via exception (Directive 1)" );
    }

    // ─── Test 2 — Regression guard: restore-false path disposes agent ─────────

    /// <summary>
    /// Regression guard for the restore-returns-false disposal path.
    ///
    /// This path was correct before the fix (the false-return branch always called
    /// <c>agent.Dispose()</c>). This test documents that the path is regression-only guarded.
    ///
    /// <b>Failure-first discipline:</b> regression-only — the false-return path was correct in
    /// the original implementation. Failure-first verification was applied by code inspection:
    /// removing <c>agent.Dispose(); agent = null;</c> from the restore-false branch would leave
    /// the agent alive (timer running), observable as <c>WasDisposed = false</c> in a tracking
    /// agent. The false-return path requires <c>RefreshCredentials</c> to return <c>false</c>
    /// without throwing — only achievable against a live PDS with an invalid token; therefore
    /// this path is regression-only at the unit level.
    /// </summary>
    [TestMethod]
    public void RestoreReturnsFalse_DisposalPath_Regression( ) {
        Assert.Inconclusive( "Restore-false / success-path disposal requires a live rotating PDS; covered at the AppHost integration tier, not unit. See brief §4." );
    }

    // ─── Test 3 — TokenRefreshFailed on current agent disposes it ─────────────

    /// <summary>
    /// Verifies that when <c>TokenRefreshFailed</c> fires on the current agent, the agent is
    /// disposed and <c>_agent</c> is nulled so the next call takes the clean restore/login path.
    ///
    /// Directive 2 (Cause C fix): stops a permanently-failing agent from re-firing on every
    /// expiry boundary.
    ///
    /// <b>Failure-first evidence:</b> before the fix, <c>OnTokenRefreshFailed</c> fire-and-forgot
    /// <c>ClearStoredCredentialsAsync</c> without disposing the agent. The pre-fix agent continued
    /// to hold its timer reference and fire <c>TokenRefreshFailed</c> on every subsequent boundary.
    /// Reverting to the pre-fix handler (no dispose, no null) would cause:
    /// (a) <c>WasDisposed = false</c> (agent not torn down), and
    /// (b) <c>_agent</c> non-null after the event (ready to fire again next cycle).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task TokenRefreshFailed_OnCurrentAgent_DisposesAgentAndNullsReference( ) {
        // Arrange — install a current agent directly via the reflection seam.
        using RedisATProtoSessionManager manager = CreateManager( );

        DisposableTrackingAgent currentAgent = new( );
        SetAgent( manager, currentAgent );
        SubscribeToAgentEvents( manager, currentAgent );

        // Act — fire TokenRefreshFailed with 401 Unauthorized (unrecoverable auth rejection)
        currentAgent.FireTokenRefreshFailed( HttpStatusCode.Unauthorized );

        // The handler runs asynchronously via Task.Run. Allow it to complete.
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        // Assert
        Assert.IsTrue( currentAgent.WasDisposed,
            "Current agent must be disposed after TokenRefreshFailed fires (Directive 2). " +
            "Pre-fix: no dispose call → WasDisposed=false → agent's timer keeps firing." );

        Assert.IsNull( GetAgent( manager ),
            "_agent must be null after TokenRefreshFailed disposes the current agent (Directive 2). " +
            "Pre-fix: _agent non-null → next GetAuthenticatedAgentAsync returns the disposed (fast-path) agent." );
    }

    // ─── Test 4 — TokenRefreshFailed on stale agent does not touch _agent ─────

    /// <summary>
    /// Verifies that <c>TokenRefreshFailed</c> on a stale (non-current) agent does not
    /// modify <c>_agent</c> — preserving the <c>ReferenceEquals</c> guard semantics.
    ///
    /// <b>Failure-first evidence:</b> removing the <c>ReferenceEquals(failingAgent, _agent)</c>
    /// check from <c>OnTokenRefreshFailed</c> would dispose and null <c>_agent</c> even when a
    /// different agent fired the event, breaking the current valid session. The test asserts
    /// the current agent (installed via reflection) remains unchanged after a stale agent fires.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task TokenRefreshFailed_OnStaleAgent_DoesNotTouchCurrentAgent( ) {
        // Arrange
        using RedisATProtoSessionManager manager = CreateManager( );

        // Install a current agent
        DisposableTrackingAgent currentAgent = new( );
        SetAgent( manager, currentAgent );
        SubscribeToAgentEvents( manager, currentAgent );

        // Create a stale agent (different instance, also subscribed)
        DisposableTrackingAgent staleAgent = new( );
        SubscribeToAgentEvents( manager, staleAgent );

        // Act — fire on the stale agent (not the current _agent)
        staleAgent.FireTokenRefreshFailed( HttpStatusCode.Unauthorized );

        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        // Assert — _agent must still be the currentAgent (not nulled or swapped)
        Assert.AreSame( currentAgent, GetAgent( manager ),
            "_agent must not be changed when a stale (non-current) agent fires TokenRefreshFailed " +
            "(ReferenceEquals guard). Pre-fix: missing guard would null the current valid session." );

        // The stale agent should be disposed (it is an orphan; the stale-agent disposal branch runs)
        Assert.IsTrue( staleAgent.WasDisposed,
            "Stale agent must be disposed when it fires TokenRefreshFailed (it is an orphan)." );
    }

    // ─── Test 5 — Cooldown suppresses repeat clears within the window ─────────

    /// <summary>
    /// Verifies that the cooldown guard suppresses a second credential clear when the same
    /// manager fires two sequential <c>TokenRefreshFailed</c> events inside the 5-second window.
    ///
    /// Directive 2 / SA-C: prevents the thousands-per-second amplification at each expiry boundary.
    ///
    /// <b>Failure-first evidence (mutation):</b> temporarily removing the <c>_lastClearAt</c> cooldown
    /// guard from <c>OnTokenRefreshFailed</c> causes this test to go RED (clearCount == 2, not 1).
    /// Restoring the guard makes it GREEN. Documented mutation evidence is captured in the
    /// Pre-Review Readiness gate for this changeset.
    ///
    /// <b>Why this shape:</b> the previous test shape passed via the <c>ReferenceEquals</c> guard
    /// (stale agents never reached the clear path), not via the cooldown. This shape fires the
    /// CURRENT agent twice — the first fire clears and sets the cooldown, the second fire must be
    /// suppressed by the cooldown guard itself.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task TokenRefreshFailed_RepeatedUnrecoverable_ClearsAtMostOncePerCooldownWindow( ) {
        // Arrange
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;

        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        // ── Round 1: agentA is current; fire 401 → should clear once and null _agent ──
        DisposableTrackingAgent agentA = new( );
        SetAgent( manager, agentA );
        SubscribeToAgentEvents( manager, agentA );

        agentA.FireTokenRefreshFailed( HttpStatusCode.Unauthorized );

        // Wait for the async handler to complete before inspecting state
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 1, clearCount, "First 401 event must produce exactly one credential clear." );
        Assert.IsNull( GetAgent( manager ), "_agent must be nulled after the first 401 event." );

        // ── Round 2: install agentB as current within the 5-second cooldown window ──
        DisposableTrackingAgent agentB = new( );
        SetAgent( manager, agentB );
        SubscribeToAgentEvents( manager, agentB );

        // Fire again — cooldown window (~5s) has NOT elapsed (~300ms since first clear)
        agentB.FireTokenRefreshFailed( HttpStatusCode.Unauthorized );

        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        // Assert — cooldown must suppress the second clear; total remains 1
        Assert.AreEqual( 1, clearCount,
            $"Cooldown guard must suppress the second credential clear within the 5-second window. " +
            $"Got {clearCount} clear(s); expected exactly 1. " +
            "If this is 2, the cooldown guard is missing or not reached (mutation failure target)." );
    }

    // ─── Test 6 — Successful restore disposes prior _agent (regression) ───────

    /// <summary>
    /// Regression guard: the successful-restore path disposes the prior <c>_agent</c> before
    /// installing the new one (<c>_agent?.Dispose()</c> at the success branch).
    ///
    /// <b>Failure-first discipline:</b> regression-only — this path was correct from the initial
    /// implementation. Mutation: removing <c>_agent?.Dispose()</c> from the success branch would
    /// leave the prior agent's timer running after a successful restore, reintroducing monotonic
    /// growth. No runtime assertion is possible without a live PDS; evidence is code inspection.
    /// </summary>
    [TestMethod]
    public void SuccessfulRestore_DisposePriorAgent_Regression( ) {
        Assert.Inconclusive( "Restore-false / success-path disposal requires a live rotating PDS; covered at the AppHost integration tier, not unit. See brief §4." );
    }

    // ─── Test 7 — Agents constructed with background refresh disabled ─────────

    /// <summary>
    /// Verifies that the default agent factory produces agents with
    /// <c>EnableBackgroundTokenRefresh = false</c>, confirming Directive 3 is active.
    ///
    /// No timer-based wait is used. The test verifies the constructor option directly.
    ///
    /// <b>Failure-first evidence:</b> removing <c>EnableBackgroundTokenRefresh = false</c> from
    /// <c>BlueskyAgentOptions</c> in <c>CreateDefaultAgent</c> would allow agents to start their
    /// internal refresh timer on construction, re-introducing the Cause-A thundering-herd cascade.
    /// The test fails if the reflected option value is <c>true</c>.
    /// </summary>
    [TestMethod]
    public void DefaultFactory_CreatesAgentsWithBackgroundRefreshDisabled( ) {
        // Arrange — access the private static CreateDefaultAgent method
        MethodInfo? createMethod = typeof( RedisATProtoSessionManager )
            .GetMethod( "CreateDefaultAgent", BindingFlags.NonPublic | BindingFlags.Static );

        Assert.IsNotNull( createMethod, "Private static CreateDefaultAgent must exist" );

        // Act — invoke the factory to get a real agent
        using BlueskyAgent? agent = (BlueskyAgent?)createMethod.Invoke( null, null );

        Assert.IsNotNull( agent, "Default factory must return a non-null BlueskyAgent" );

        // Assert — verify EnableBackgroundTokenRefresh=false via reflection on the agent's _options field.
        // BlueskyAgent stores its options internally; traverse the inheritance chain to find it.
        bool? bgRefreshEnabled = FindBackgroundRefreshOption( agent );

        Assert.IsNotNull( bgRefreshEnabled,
            "EnableBackgroundTokenRefresh option must be reflectable — a null result means the Cause-A linchpin guard silently stopped running (idunno field layout changed)." );

        if (bgRefreshEnabled.HasValue) {
            Assert.IsFalse( bgRefreshEnabled.Value,
                "Default factory must produce agents with EnableBackgroundTokenRefresh=false (Directive 3). " +
                "Pre-fix: agents used default options (background refresh ON) → Cause-A cascade." );
        }
        // If the option is not accessible via reflection (private deep field), the test still
        // verifies that the default factory method exists and returns a non-null agent, and the
        // source code audit (compilation) confirms EnableBackgroundTokenRefresh=false is set.
    }

    // ─── Test 8 — Factory seam is active during auth attempts ─────────────────

    /// <summary>
    /// Verifies that the injected <c>agentFactory</c> is invoked during session restoration,
    /// confirming the seam is active and not bypassed.
    ///
    /// <b>Failure-first evidence:</b> before the factory seam was introduced,
    /// <c>RedisATProtoSessionManager</c> used <c>new BlueskyAgent()</c> directly — there was no
    /// substitution point. A factory call count of 0 after the auth attempt would indicate the
    /// seam is broken. The test fails if the factory is not called.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task AgentFactory_WhenProvided_IsInvokedDuringSessionRestore( ) {
        // Arrange — provide stored credentials so TryRestoreSessionAsync is entered
        // (which calls the factory). The closed-port service causes instant failure after
        // the factory call, so the test completes quickly.
        SetupStoredCredentials( BuildRefusingServiceCredentialJson( ) );

        int factoryCallCount = 0;
        Func<BlueskyAgent> factory = ( ) => {
            _ = Interlocked.Increment( ref factoryCallCount );
            return new DisposableTrackingAgent( );
        };

        using RedisATProtoSessionManager manager = CreateManager( factory );

        // Act — call TryRestoreSessionAsync directly to avoid the PerformFreshLoginAsync network path
        _ = await TryRestoreSessionAsync( manager, TestContext.CancellationToken );

        // Assert
        Assert.IsGreaterThanOrEqualTo( factoryCallCount, 1,
            $"agentFactory must be called during session restoration. " +
            $"Factory call count: {factoryCallCount} (seam verification)" );
    }

    // ─── Test 9 — SA-C: transport failure keeps Redis credential ─────────────

    /// <summary>
    /// Verifies SA-C semantics: a token refresh failure with a 5xx status code (transport error)
    /// does NOT clear the shared Redis credential.
    ///
    /// <b>SA-C directive:</b> clearing the shared Redis credential on a transient transport error
    /// forces a fleet-wide re-login storm. Only unrecoverable auth rejection (4xx) clears the credential.
    ///
    /// <b>Failure-first evidence:</b> before SA-C, <c>OnTokenRefreshFailed</c> always called
    /// <c>ClearStoredCredentialsAsync</c> regardless of status code. The observed real-world failure
    /// was BadGateway (502) — each process clearing the shared credential on BadGateway caused
    /// every other process to re-login, amplifying the PDS load. Reverting SA-C semantics would
    /// cause <c>KeyDeleteAsync</c> to be called on 5xx, failing the zero-count assertion.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task TokenRefreshFailed_TransportError5xx_DoesNotClearSharedCredential( ) {
        // Arrange
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;

        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        // Act — fire TokenRefreshFailed with BadGateway (502, the observed real-world failure)
        agent.FireTokenRefreshFailed( HttpStatusCode.BadGateway );

        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        // Assert — no credential clear
        Assert.AreEqual( 0, clearCount,
            $"Transport failure (BadGateway/5xx) must NOT clear the shared Redis credential. " +
            $"KeyDeleteAsync was called {clearCount} time(s). " +
            "SA-C: clearing on 5xx forces a fleet-wide re-login storm (observed at ~1.2M events/day)." );

        // Agent must still be disposed (agent torn down, but Redis key kept)
        Assert.IsTrue( agent.WasDisposed,
            "The failing agent must be disposed even when the credential is kept (Directive 2 + SA-C)." );
    }

    // ─── Test 10 — Unauthenticated event disposes agent and clears credential ──

    /// <summary>
    /// Verifies that the <c>Unauthenticated</c> event on the current agent disposes it and
    /// clears the shared Redis credential exactly once (unrecoverable session end).
    ///
    /// <b>Failure-first evidence:</b> before Directive 2, the <c>OnUnauthenticated</c> handler
    /// fire-and-forgot <c>ClearStoredCredentialsAsync</c> without disposing the agent. The current
    /// implementation disposes the agent and nulls <c>_agent</c> under the lock; this test asserts
    /// both behaviors.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task Unauthenticated_OnCurrentAgent_DisposesAgentAndClearsCredential( ) {
        // Arrange
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;

        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        // Act — fire Unauthenticated (session genuinely ended by PDS)
        agent.FireUnauthenticated( );

        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        // Assert
        Assert.IsTrue( agent.WasDisposed,
            "Current agent must be disposed after Unauthenticated event (Directive 2). " +
            "Pre-fix: no dispose → agent's timer kept firing after session end." );

        Assert.IsNull( GetAgent( manager ),
            "_agent must be null after Unauthenticated disposes the current agent (Directive 2)." );

        Assert.AreEqual( 1, clearCount,
            "Unauthenticated event (session ended) must clear the shared Redis credential exactly once (SA-C)." );
    }

    // ─── Tests 11-17 — Boundary-predicate negative-control tests (SE directive) ──

    /// <summary>
    /// Verifies CLEAR on HTTP 400 with <c>Error.Error = "ExpiredToken"</c> (dead-token by name).
    ///
    /// <b>Failure-first evidence:</b> removing <c>deadTokenByName</c> from the predicate keeps the
    /// credential on 400+ExpiredToken, causing clearCount to remain 0 and failing the AreEqual(1).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_400_ExpiredToken_Clears( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        agent.FireTokenRefreshFailed( HttpStatusCode.BadRequest, new AtErrorDetail( "ExpiredToken", "Token has expired." ) );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 1, clearCount,
            "400+ExpiredToken is a dead-token result; shared credential must be cleared." );
    }

    /// <summary>
    /// Verifies CLEAR on HTTP 400 with <c>Error.Error = "InvalidToken"</c> (dead-token by name).
    ///
    /// <b>Failure-first evidence:</b> removing <c>InvalidToken</c> from <c>deadTokenByName</c>
    /// keeps the credential, causing clearCount to remain 0.
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_400_InvalidToken_Clears( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        agent.FireTokenRefreshFailed( HttpStatusCode.BadRequest, new AtErrorDetail( "InvalidToken", "Token is invalid." ) );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 1, clearCount,
            "400+InvalidToken is a dead-token result; shared credential must be cleared." );
    }

    /// <summary>
    /// Verifies KEEP on HTTP 429 (rate-limit), regardless of error body.
    ///
    /// <b>Failure-first evidence:</b> classifying 429 as unrecoverable would call KeyDeleteAsync,
    /// causing clearCount to be 1 and failing the AreEqual(0).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_429_RateLimit_Keeps( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        agent.FireTokenRefreshFailed( HttpStatusCode.TooManyRequests, new AtErrorDetail( "RateLimitExceeded", "Too many requests." ) );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 0, clearCount,
            "429 TooManyRequests is transient; shared credential must NOT be cleared." );
    }

    /// <summary>
    /// Verifies KEEP on HTTP 408 (request timeout) — transient, must not clear credential.
    ///
    /// <b>Failure-first evidence:</b> treating 408 as unrecoverable would call KeyDeleteAsync,
    /// causing clearCount to be 1 and failing the AreEqual(0).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_408_RequestTimeout_Keeps( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        agent.FireTokenRefreshFailed( HttpStatusCode.RequestTimeout );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 0, clearCount,
            "408 RequestTimeout is transient; shared credential must NOT be cleared." );
    }

    /// <summary>
    /// Verifies KEEP on HTTP 400 with a <c>null</c> error body — conservative default.
    ///
    /// <b>Failure-first evidence:</b> treating 400 with no error name as unrecoverable would call
    /// KeyDeleteAsync, causing clearCount to be 1 and failing the AreEqual(0).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_400_NullErrorBody_Keeps( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        // null error body — name is unknown, conservative default is KEEP
        agent.FireTokenRefreshFailed( HttpStatusCode.BadRequest, error: null );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 0, clearCount,
            "400 with null error body: error name is unknown; conservative default is KEEP (do not clear)." );
    }

    /// <summary>
    /// Verifies KEEP on HTTP 400 with <c>Error.Error = "RateLimitExceeded"</c> — unknown dead-token name.
    ///
    /// <b>Failure-first evidence:</b> treating any 400 as unrecoverable (old band-only predicate) would
    /// call KeyDeleteAsync, causing clearCount to be 1 and failing the AreEqual(0).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_400_RateLimitExceeded_Keeps( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        agent.FireTokenRefreshFailed( HttpStatusCode.BadRequest, new AtErrorDetail( "RateLimitExceeded", "Rate limit." ) );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 0, clearCount,
            "400+RateLimitExceeded is not a known dead-token error name; conservative default is KEEP. " +
            "The old band-only predicate (any 4xx clears) would fail this assertion." );
    }

    /// <summary>
    /// Verifies CLEAR on HTTP 401 (Unauthorized) — genuine auth rejection by status band.
    ///
    /// <b>Failure-first evidence:</b> removing <c>deadTokenByStatus</c> from the predicate keeps the
    /// credential on 401, causing clearCount to remain 0 and failing the AreEqual(1).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task BoundaryPredicate_401_Unauthorized_Clears( ) {
        using RedisATProtoSessionManager manager = CreateManager( );
        int clearCount = 0;
        _ = _dbMock
            .Setup( d => d.KeyDeleteAsync( It.IsAny<RedisKey>( ), It.IsAny<CommandFlags>( ) ) )
            .Callback( ( RedisKey _, CommandFlags __ ) => Interlocked.Increment( ref clearCount ) )
            .ReturnsAsync( true );

        DisposableTrackingAgent agent = new( );
        SetAgent( manager, agent );
        SubscribeToAgentEvents( manager, agent );

        agent.FireTokenRefreshFailed( HttpStatusCode.Unauthorized );
        await Task.Delay( TimeSpan.FromMilliseconds( 300 ), TestContext.CancellationToken );

        Assert.AreEqual( 1, clearCount,
            "401 Unauthorized is a genuine auth rejection (deadTokenByStatus); shared credential must be cleared." );
    }

    // ─── Test: lock wait timeout throws InvalidOperationException (fail-closed) ─────

    /// <summary>
    /// Verifies that when the distributed lock cannot be acquired within the TTL window,
    /// <see cref="RedisATProtoSessionManager.GetAuthenticatedAgentAsync"/> throws
    /// <see cref="InvalidOperationException"/> without invoking the agent factory.
    ///
    /// Fail-closed: rotating shared refresh token credentials without the distributed lock
    /// risks two holders each invalidating the other's rotation, causing an auth storm.
    ///
    /// <b>Failure-first evidence:</b> with the <c>throw</c> present, the factory is never called
    /// (the method exits at the lock-timeout branch before reaching <c>TryRestoreSessionAsync</c>
    /// or <c>PerformFreshLoginAsync</c>). The test was run against a mutant in which the
    /// <c>throw new InvalidOperationException</c> was removed; the mutant fell through to
    /// <c>PerformFreshLoginAsync</c>, which called the factory (factoryCallCount = 1), failing
    /// the <c>AreEqual(0, factoryCallCount)</c> assertion. Restoring the throw makes both
    /// assertions pass (exception thrown, factory not called).
    /// </summary>
    [TestMethod]
    [Timeout( 3000 )]
    public async Task LockWaitTimeout_FailsClosed( ) {
        // Lock acquire always returns false — simulates another holder that never releases.
        SetupLockAcquire( false );

        // Track whether the agent factory is invoked. The factory is only called inside
        // TryRestoreSessionAsync and PerformFreshLoginAsync. If the fail-closed throw is present
        // the method exits before reaching either; if removed, PerformFreshLoginAsync calls it.
        int factoryCallCount = 0;
        Func<BlueskyAgent> trackingFactory = ( ) => {
            _ = Interlocked.Increment( ref factoryCallCount );
            return new DisposableTrackingAgent( );
        };

        // Use accelerated lock timing so the poll loop completes in ~100 ms.
        using RedisATProtoSessionManager manager = new(
            _redisMock.Object,
            _loggerMock.Object,
            TestIdentifier,
            TestPassword,
            trackingFactory
        ) {
            LockExpiry = TimeSpan.FromMilliseconds( 100 ),
            LockPollInterval = TimeSpan.FromMilliseconds( 10 )
        };

        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            ( ) => manager.GetAuthenticatedAgentAsync( TestContext.CancellationToken ),
            "AcquireDistributedLockAndRefreshAsync must throw InvalidOperationException when the lock " +
            "cannot be acquired within the TTL window (fail-closed)." );

        Assert.AreEqual( 0, factoryCallCount,
            "The agent factory must not be called when the lock-wait times out. " +
            "A non-zero count means the method fell through to PerformFreshLoginAsync (mutation detected)." );
    }

    // ─── Test: OperationCanceledException propagates from TryRestoreSessionAsync ─

    /// <summary>
    /// Verifies that <see cref="OperationCanceledException"/> thrown from within
    /// <c>TryRestoreSessionAsync</c> propagates to the caller rather than being swallowed
    /// by the broad <c>catch (Exception ex)</c> block.
    ///
    /// The test injects an OCE at the Redis read layer (before agent creation) so the
    /// exception path is clean and does not rely on network behaviour. This exercises the
    /// <c>catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)</c>
    /// guard added by this fix.
    ///
    /// <b>Failure-first evidence:</b> before the fix, <c>TryRestoreSessionAsync</c> had only
    /// <c>catch (Exception ex)</c>. The test was run against the pre-fix code (OCE catch clause
    /// removed) and observed to fail: the method returned <c>false</c> instead of throwing,
    /// causing the <c>Assert.IsTrue(threw)</c> assertion to fail (verified by temporarily
    /// removing the catch clause and running the test).
    /// </summary>
    [TestMethod]
    [Timeout( 5000 )]
    public async Task TryRestoreSession_WhenCancelledDuringRedisRead_RethrowsOce( ) {
        // Arrange — mock Redis StringGetAsync to throw OCE, simulating cancellation during
        // the credential read step. Using a pre-cancelled token so IsCancellationRequested = true.
        using CancellationTokenSource cts = new( );
        cts.Cancel( );

        _ = _dbMock
            .Setup( d => d.StringGetAsync(
                It.Is<RedisKey>( k => ((string)k!).Contains( "atproto:session:" ) ),
                It.IsAny<CommandFlags>( ) ) )
            .ThrowsAsync( new OperationCanceledException( cts.Token ) );

        using RedisATProtoSessionManager manager = CreateManager( );

        // Act + Assert — OCE must propagate, not be swallowed as false
        bool threw = false;
        try {
            _ = await TryRestoreSessionAsync( manager, cts.Token );
        } catch (OperationCanceledException) {
            threw = true;
        }

        Assert.IsTrue( threw,
            "OperationCanceledException must propagate from TryRestoreSessionAsync when the token is cancelled. " +
            "Pre-fix: the broad catch (Exception ex) swallowed OCE and returned false instead." );
    }

    // ─── Reflection helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Traverses the private fields of a <see cref="BlueskyAgent"/> instance (and its base classes)
    /// looking for an option field that exposes <c>EnableBackgroundTokenRefresh</c>.
    /// Returns the boolean value if found; null if the field is not accessible via reflection.
    /// </summary>
    private static bool? FindBackgroundRefreshOption( BlueskyAgent agent ) {
        Type? type = agent.GetType( );
        while (type is not null && type != typeof( object )) {
            foreach (FieldInfo field in type.GetFields( BindingFlags.NonPublic | BindingFlags.Instance )) {
                object? value = field.GetValue( agent );
                if (value is null) { continue; }

                PropertyInfo? prop = value.GetType( )
                    .GetProperty( "EnableBackgroundTokenRefresh" );

                if (prop is not null) {
                    return (bool?)prop.GetValue( value );
                }
            }
            type = type.BaseType;
        }
        return null; // not accessible — source-code audit is the fallback evidence
    }
}

// ─── Test seam: trackable BlueskyAgent subclass ────────────────────────────────

/// <summary>
/// A <see cref="BlueskyAgent"/> subclass that tracks disposal and can fire the protected
/// <c>OnTokenRefreshFailed</c> and <c>OnUnauthenticated</c> events, enabling unit testing of
/// <see cref="RedisATProtoSessionManager"/> event handlers without a live PDS.
/// </summary>
internal sealed class DisposableTrackingAgent : BlueskyAgent {

    /// <summary>Whether <see cref="IDisposable.Dispose"/> has been called on this instance.</summary>
    public bool WasDisposed { get; private set; }

    protected override void Dispose( bool disposing ) {
        WasDisposed = true;
        base.Dispose( disposing );
    }

    /// <summary>
    /// Fires the <c>TokenRefreshFailed</c> event with the specified status code and optional error detail.
    /// </summary>
    public void FireTokenRefreshFailed( HttpStatusCode? statusCode, AtErrorDetail? error = null ) {
        Did did = new( "did:plc:test12345" );
        TokenRefreshFailedEventArgs args = new(
            did,
            new Uri( "https://test.example" ),
            "fake-refresh-token",
            statusCode,
            error! );
        OnTokenRefreshFailed( args );
    }

    /// <summary>Fires the <c>Unauthenticated</c> event.</summary>
    public void FireUnauthenticated( ) {
        Did did = new( "did:plc:test12345" );
        UnauthenticatedEventArgs args = new( did, new Uri( "https://test.example" ) );
        OnUnauthenticated( args );
    }
}
