using System.Net;
using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using idunno.AtProto;
using idunno.AtProto.Authentication;
using idunno.AtProto.Events;
using idunno.Bluesky;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// Redis-backed implementation of <see cref="IATProtoSessionManager"/> that centralizes
/// ATProto session management across all service instances.
/// </summary>
/// <remarks>
/// Session credentials are persisted to Redis and shared across worker instances. Token rotation
/// is serialized via a distributed lock so only one process calls <c>refreshSession</c> at a time.
/// Key pattern: <c>atproto:session:{identifier}</c>.
/// </remarks>
public sealed partial class RedisATProtoSessionManager : IATProtoSessionManager, IDisposable {

    private const string SessionKeyPrefix = "atproto:session:";
    private const string LockKeyPrefix = "atproto:auth:lock:";

    // s_refreshTimeout < s_lockExpiry / 2 — worst-case refresh+login pair (2×12s=24s) must stay under lock TTL (30s).
    private static readonly TimeSpan s_lockExpiry = TimeSpan.FromSeconds( 30 );
    private static readonly TimeSpan s_refreshTimeout = TimeSpan.FromSeconds( 12 );
    private static readonly TimeSpan s_lockPollInterval = TimeSpan.FromMilliseconds( 200 );
    private static readonly TimeSpan s_cooldownWindow = TimeSpan.FromSeconds( 5 );

    // Lua script for atomic compare-and-delete of the distributed lock.
    // Returns 1 if the key was deleted (we still owned it), 0 otherwise.
    private const string ReleaseLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly SemaphoreSlim _agentLock = new( 1, 1 );
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisATProtoSessionManager> _logger;
    private readonly string _identifier;
    private readonly string _password;
    private readonly string _sessionKey;
    private readonly string _lockKey;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<BlueskyAgent> _agentFactory;

    private BlueskyAgent? _agent;
    private DateTimeOffset _lastClearAt = DateTimeOffset.MinValue;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisATProtoSessionManager"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="identifier">The ATProto account identifier (handle or DID).</param>
    /// <param name="password">The ATProto app password.</param>
    /// <param name="agentFactory">
    /// Optional factory for creating <see cref="BlueskyAgent"/> instances.
    /// Defaults to creating agents with background token refresh disabled and a
    /// <see cref="s_refreshTimeout"/>-bounded HTTP timeout.
    /// </param>
    public RedisATProtoSessionManager(
        IConnectionMultiplexer redis,
        ILogger<RedisATProtoSessionManager> logger,
        string identifier,
        string password,
        Func<BlueskyAgent>? agentFactory = null
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _identifier = identifier ?? throw new ArgumentNullException( nameof( identifier ) );
        _password = password ?? throw new ArgumentNullException( nameof( password ) );

        _sessionKey = $"{SessionKeyPrefix}{identifier}";
        _lockKey = $"{LockKeyPrefix}{identifier}";

        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _agentFactory = agentFactory ?? CreateDefaultAgent;
    }

    /// <inheritdoc/>
    public async Task<BlueskyAgent> GetAuthenticatedAgentAsync( CancellationToken cancellationToken = default ) {
        ObjectDisposedException.ThrowIf( _disposed, this );

        // Fast path: return existing authenticated agent (no lock needed for read)
        BlueskyAgent? currentAgent = _agent;
        if (currentAgent is { IsAuthenticated: true }) {
            return currentAgent;
        }

        // Slow path: acquire in-process lock first, then the distributed Redis lock.
        // Lock ordering: _agentLock (in-process) → distributed Redis lock (cross-process).
        // This ordering is consistent everywhere both locks are held.
        await _agentLock.WaitAsync( cancellationToken );
        try {
            // Double-check after acquiring in-process lock
            currentAgent = _agent;
            if (currentAgent is { IsAuthenticated: true }) {
                return currentAgent;
            }

            // Acquire the distributed lock and perform credential mutation under it.
            return await AcquireDistributedLockAndRefreshAsync( cancellationToken );
        } finally {
            _ = _agentLock.Release( );
        }
    }

    /// <inheritdoc/>
    public async Task<BlueskyAgent> ForceReauthenticateAsync( CancellationToken cancellationToken = default ) {
        ObjectDisposedException.ThrowIf( _disposed, this );

        LogForceReauthRequested( _identifier );

        // Acquire in-process lock before the distributed lock (consistent ordering).
        await _agentLock.WaitAsync( cancellationToken );
        try {
            // Clear stored credentials
            await ClearStoredCredentialsAsync( );

            // Dispose existing agent if any
            BlueskyAgent? oldAgent = _agent;
            _agent = null;
            oldAgent?.Dispose( );

            // Acquire the distributed lock and perform a fresh login.
            return await AcquireDistributedLockAndRefreshAsync( cancellationToken );
        } finally {
            _ = _agentLock.Release( );
        }
    }

    /// <summary>
    /// Acquires the distributed Redis credential lock, then restores or logs in.
    /// Lock-losers block-acquire and rotate sequentially.
    /// </summary>
    private async Task<BlueskyAgent> AcquireDistributedLockAndRefreshAsync( CancellationToken cancellationToken ) {
        IDatabase db = _redis.GetDatabase( );
        string lockValue = Guid.NewGuid( ).ToString( );

        // Block-acquire the distributed lock: try immediately, then poll until acquired
        // or the lock TTL elapses (covers the crash-while-holding case).
        bool lockAcquired = await db.StringSetAsync( _lockKey, lockValue, s_lockExpiry, When.NotExists );

        if (!lockAcquired) {
            LogWaitingForLock( _identifier );
            TimeSpan waited = TimeSpan.Zero;
            while (!lockAcquired && waited < s_lockExpiry) {
                await Task.Delay( s_lockPollInterval, cancellationToken );
                waited += s_lockPollInterval;
                lockAcquired = await db.StringSetAsync( _lockKey, lockValue, s_lockExpiry, When.NotExists );
            }

            if (!lockAcquired) {
                // Lock TTL elapsed without release — either the holder crashed and the TTL has
                // not yet expired on this Redis call, or something is very wrong. Log and attempt
                // anyway; worst case the two concurrent holders each rotate once.
                LogLockWaitTimeout( _identifier );
            }
        }

        try {
            // Double-check after acquiring the distributed lock: another process may have
            // refreshed and the in-process _agent may have been updated via OnCredentialsUpdated.
            if (_agent is { IsAuthenticated: true }) {
                return _agent;
            }

            // Try to restore session from Redis (includes calling RefreshCredentials under the lock).
            if (await TryRestoreSessionAsync( cancellationToken )) {
                return _agent!;
            }

            // No stored session or restoration failed — perform a fresh login under the lock.
            return await PerformFreshLoginAsync( cancellationToken );
        } finally {
            if (lockAcquired) {
                await ReleaseLockAsync( db, lockValue );
            }
        }
    }

    /// <summary>
    /// Attempts to restore a session from Redis-stored credentials.
    /// Callers must hold the distributed Redis lock before calling this method.
    /// </summary>
    /// <returns>True if session was successfully restored, false otherwise.</returns>
    private async Task<bool> TryRestoreSessionAsync( CancellationToken cancellationToken ) {
        BlueskyAgent? agent = null;
        try {
            IDatabase db = _redis.GetDatabase( );
            RedisValue storedCredentials = await db.StringGetAsync( _sessionKey );

            if (storedCredentials.IsNullOrEmpty) {
                LogNoStoredCredentials( _identifier );
                return false;
            }

            ATProtoPersistedCredentials? credentials = JsonSerializer.Deserialize<ATProtoPersistedCredentials>(
                storedCredentials.ToString( ),
                _jsonOptions
            );

            if (credentials is null) {
                LogDeserializeFailed( _identifier );
                return false;
            }

            LogRestoringSession( _identifier, credentials.PersistedAt );

            // Create a new agent (background token refresh disabled) and attempt session restoration.
            agent = _agentFactory( );
            SubscribeToAgentEvents( agent );

            // Parse authentication type
            if (!Enum.TryParse<AuthenticationType>( credentials.AuthenticationType, out AuthenticationType authType )) {
                authType = AuthenticationType.UsernamePassword;
            }

            // Create credential for session restoration
            AtProtoCredential restoredCredential = AtProtoCredential.Create(
                service: new Uri( credentials.Service ),
                authenticationType: authType,
                refreshToken: credentials.RefreshToken,
                dPoPProofKey: credentials.DPoPProofKey,
                dPoPNonce: credentials.DPoPNonce
            );

            // RefreshCredentials rotates the refresh token (single-use); this call runs under the
            // distributed lock so only one process rotates at a time.
            bool refreshResult = await agent.RefreshCredentials( restoredCredential, cancellationToken );

            if (refreshResult) {
                LogSessionRestored( _identifier );
                // Persist the rotated token before transferring ownership so lock-losers reuse the new token.
                await PersistCredentialsFromAgentAsync( agent );
                _agent?.Dispose( );
                _agent = agent;
                agent = null; // ownership transferred — suppress disposal in finally
                return true;
            }

            LogRestoreFailed( _identifier );
            agent.Dispose( );
            agent = null;
            return false;
        } catch (Exception ex) {
            LogRestoreException( ex, _identifier );
            agent?.Dispose( );
            return false;
        }
    }

    /// <summary>
    /// Performs a fresh login with the stored identifier and password.
    /// Callers must hold the distributed Redis lock before calling this method.
    /// </summary>
    private async Task<BlueskyAgent> PerformFreshLoginAsync( CancellationToken cancellationToken ) {
        BlueskyAgent? agent = null;
        try {
            agent = _agentFactory( );
            SubscribeToAgentEvents( agent );

            LogFreshLogin( _identifier );

            AtProtoHttpResult<bool> loginResult = await agent.Login( _identifier, _password, cancellationToken: cancellationToken );

            if (!loginResult.Succeeded) {
                string errorMsg = loginResult.AtErrorDetail?.Message ?? $"HTTP {loginResult.StatusCode}";
                agent.Dispose( );
                agent = null;
                throw new InvalidOperationException( $"Failed to authenticate to PDS: {errorMsg}" );
            }

            LogAuthenticated( _identifier );

            // Persist credentials (the Authenticated event handler will also do this, but we
            // do it explicitly for safety in case the event fires before persistence completes).
            await PersistCredentialsFromAgentAsync( agent );

            _agent?.Dispose( );
            _agent = agent;
            agent = null; // ownership transferred

            return _agent!;
        } catch {
            agent?.Dispose( );
            throw;
        }
    }

    /// <summary>
    /// Subscribes to agent authentication events to persist credential updates.
    /// </summary>
    private void SubscribeToAgentEvents( BlueskyAgent agent ) {
        agent.Authenticated += OnAuthenticated;
        agent.CredentialsUpdated += OnCredentialsUpdated;
        agent.TokenRefreshFailed += OnTokenRefreshFailed;
        agent.Unauthenticated += OnUnauthenticated;
    }

    /// <summary>
    /// Handles the Authenticated event - persists initial credentials.
    /// </summary>
    private void OnAuthenticated( object? sender, AuthenticatedEventArgs e ) {
        LogAgentAuthenticated( _identifier );

        if (sender is BlueskyAgent agent) {
            // Fire and forget - don't block the event
            _ = Task.Run( async ( ) => {
                try {
                    await PersistCredentialsFromAgentAsync( agent );
                } catch (Exception ex) {
                    LogPersistAfterAuthFailed( ex, _identifier );
                }
            } );
        }
    }

    /// <summary>
    /// Handles the CredentialsUpdated event - persists refreshed credentials.
    /// </summary>
    private void OnCredentialsUpdated( object? sender, CredentialsUpdatedEventArgs e ) {
        LogCredentialsUpdated( _identifier );

        if (sender is BlueskyAgent agent) {
            // Check if this agent is still the current agent before persisting
            // This prevents ObjectDisposedException when a stale agent fires events
            if (!ReferenceEquals( agent, _agent )) {
                return;
            }

            // Fire and forget - don't block the event
            _ = Task.Run( async ( ) => {
                try {
                    // Double-check inside the task in case agent was swapped during scheduling
                    if (!ReferenceEquals( agent, _agent )) {
                        return;
                    }

                    await PersistCredentialsFromAgentAsync( agent );
                } catch (Exception ex) {
                    LogPersistUpdateFailed( ex, _identifier );
                }
            } );
        }
    }

    /// <summary>
    /// Handles the TokenRefreshFailed event.
    /// Disposes the failing agent if it is still current, and clears the shared Redis credential
    /// only on a genuine auth rejection (dead-token error name per the refreshSession lexicon,
    /// or 401/403). Transient transport failures (5xx/null) keep the credential so other
    /// processes are not forced into a re-login storm.
    /// </summary>
    private void OnTokenRefreshFailed( object? sender, TokenRefreshFailedEventArgs e ) {
        // Classify the refresh failure: clear the SHARED credential only when the refresh
        // token is genuinely dead. Decide on the XRPC error NAME first (it is authoritative
        // and the dead-token cases are HTTP 400, not 401); fall back to status band only when
        // no error body is present.
        //
        // Dead-token error names per com.atproto.server.refreshSession lexicon.
        string? errorName = e.Error?.Error;
        int? status = e.StatusCode.HasValue ? (int)e.StatusCode.Value : null;

        bool deadTokenByName =
            string.Equals( errorName, "ExpiredToken",    StringComparison.OrdinalIgnoreCase ) ||
            string.Equals( errorName, "InvalidToken",    StringComparison.OrdinalIgnoreCase ) ||
            string.Equals( errorName, "AccountTakedown", StringComparison.OrdinalIgnoreCase );

        // 401/403 with no/unknown error name are still genuine auth rejections.
        bool deadTokenByStatus = status is 401 or 403;

        // Everything else is transient and must NOT clear the shared credential:
        //   - 408 (timeout), 429 (rate limit), any 5xx, null status (transport failure)
        //   - any 400 whose error name is NOT a known dead-token name (conservative default: KEEP).
        bool isUnrecoverableAuthFailure = deadTokenByName || deadTokenByStatus;

        LogTokenRefreshFailed( _identifier, e.StatusCode );

        if (sender is not BlueskyAgent failingAgent) {
            return;
        }

        _ = Task.Run( async ( ) => {
            try {
                await _agentLock.WaitAsync( );
                try {
                    if (!ReferenceEquals( failingAgent, _agent )) {
                        // Stale agent — dispose it silently; do not touch _agent or Redis
                        failingAgent.Dispose( );
                        return;
                    }

                    // The failing agent is the current agent — dispose and null it so the next
                    // request takes the clean restore/login path
                    _agent = null;
                    failingAgent.Dispose( );

                    if (isUnrecoverableAuthFailure) {
                        // Genuine auth rejection — clear the shared Redis credential so all
                        // processes will re-login rather than retrying a dead token
                        DateTimeOffset now = DateTimeOffset.UtcNow;
                        if (now - _lastClearAt >= s_cooldownWindow) {
                            _lastClearAt = now;
                            await ClearStoredCredentialsAsync( );
                        } else {
                            LogClearSuppressedByCooldown( _identifier );
                        }
                    }
                    // Transport failure (5xx / null status): keep the Redis credential — it is
                    // still valid. The next request will retry under the lock with a fresh agent.
                } finally {
                    _ = _agentLock.Release( );
                }
            } catch (Exception ex) {
                LogClearAfterRefreshFailed( ex, _identifier );
            }
        } );
    }

    /// <summary>
    /// Handles the Unauthenticated event - the PDS has explicitly ended the session.
    /// This is an unrecoverable auth result; always clear the shared Redis credential
    /// (subject to the cooldown guard to prevent burst clears).
    /// </summary>
    private void OnUnauthenticated( object? sender, UnauthenticatedEventArgs e ) {
        LogUnauthenticated( _identifier );

        if (sender is not BlueskyAgent failingAgent) {
            return;
        }

        _ = Task.Run( async ( ) => {
            try {
                await _agentLock.WaitAsync( );
                try {
                    if (!ReferenceEquals( failingAgent, _agent )) {
                        failingAgent.Dispose( );
                        return;
                    }

                    _agent = null;
                    failingAgent.Dispose( );

                    // Session genuinely ended — clear the shared credential
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    if (now - _lastClearAt >= s_cooldownWindow) {
                        _lastClearAt = now;
                        await ClearStoredCredentialsAsync( );
                    } else {
                        LogClearSuppressedByCooldown( _identifier );
                    }
                } finally {
                    _ = _agentLock.Release( );
                }
            } catch (Exception ex) {
                LogClearAfterUnauthFailed( ex, _identifier );
            }
        } );
    }

    /// <summary>
    /// Persists the agent's current credentials to Redis.
    /// </summary>
    private async Task PersistCredentialsFromAgentAsync( BlueskyAgent agent ) {
        if (!agent.IsAuthenticated || agent.Credentials is null) {
            LogCannotPersist( );
            return;
        }

        AccessCredentials credentials = agent.Credentials;

        // Extract DPoP fields if available
        string? dPoPProofKey = null;
        string? dPoPNonce = null;

        if (credentials is DPoPAccessCredentials dPoPCredentials) {
            dPoPProofKey = dPoPCredentials.DPoPProofKey;
            dPoPNonce = dPoPCredentials.DPoPNonce;
        }

        ATProtoPersistedCredentials persistedCredentials = new( ) {
            RefreshToken = credentials.RefreshToken,
            DPoPProofKey = dPoPProofKey,
            DPoPNonce = dPoPNonce,
            Service = credentials.Service.ToString( ),
            Did = credentials.Did?.ToString( ) ?? string.Empty,
            Handle = _identifier,
            AuthenticationType = credentials.AuthenticationType.ToString( ),
            PersistedAt = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize( persistedCredentials, _jsonOptions );

        IDatabase db = _redis.GetDatabase( );
        _ = await db.StringSetAsync( _sessionKey, json );

        LogPersisted( _identifier );
    }

    /// <summary>
    /// Clears stored credentials from Redis.
    /// </summary>
    private async Task ClearStoredCredentialsAsync( ) {
        IDatabase db = _redis.GetDatabase( );
        _ = await db.KeyDeleteAsync( _sessionKey );
        LogCleared( _identifier );
    }

    /// <summary>
    /// Releases the distributed lock using an atomic compare-and-delete Lua script,
    /// preventing deletion of a lock that was re-acquired by another process after TTL expiry.
    /// </summary>
    private async Task ReleaseLockAsync( IDatabase db, string lockValue ) {
        try {
            _ = await db.ScriptEvaluateAsync(
                ReleaseLockScript,
                keys: [_lockKey],
                values: [lockValue]
            );
        } catch (Exception ex) {
            LogLockReleaseFailed( ex, _identifier );
        }
    }

    /// <summary>
    /// Default agent factory: creates a <see cref="BlueskyAgent"/> with background token refresh
    /// disabled and an HTTP timeout bounded below the distributed lock TTL.
    /// </summary>
    private static BlueskyAgent CreateDefaultAgent( ) =>
        new( new BlueskyAgentOptions {
            EnableBackgroundTokenRefresh = false,
            HttpClientOptions = new HttpClientOptions( timeout: s_refreshTimeout )
        } );

    /// <inheritdoc/>
    public void Dispose( ) {
        if (_disposed) { return; }

        _agent?.Dispose( );
        _agent = null;
        _agentLock.Dispose( );
        _disposed = true;
    }

}
