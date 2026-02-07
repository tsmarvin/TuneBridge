using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using idunno.AtProto;
using idunno.AtProto.Authentication;
using idunno.Bluesky;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// Redis-backed implementation of <see cref="IATProtoSessionManager"/> that centralizes
/// ATProto session management across all service instances.
/// </summary>
/// <remarks>
/// <para>
/// This service solves the problem of excessive PDS API calls (refreshSession, getSession, createSession)
/// by persisting session credentials to Redis and sharing them across all worker instances.
/// </para>
/// <para>
/// Session restoration follows the idunno.Bluesky library pattern:
/// 1. On first request, attempt to restore from Redis using <c>AtProtoCredential.Create()</c> + <c>RefreshCredentials()</c>
/// 2. Subscribe to agent events to persist credential updates
/// 3. On <c>TokenRefreshFailed</c>, clear Redis and re-login with password
/// </para>
/// <para>
/// Redis Key Pattern: <c>atproto:session:{identifier}</c> → JSON <see cref="ATProtoPersistedCredentials"/>
/// </para>
/// </remarks>
public sealed partial class RedisATProtoSessionManager : IATProtoSessionManager, IDisposable {

    private const string SessionKeyPrefix = "atproto:session:";
    private const string LockKeyPrefix = "atproto:auth:lock:";
    private static readonly TimeSpan s_lockExpiry = TimeSpan.FromSeconds( 30 );
    private readonly SemaphoreSlim _agentLock = new( 1, 1 );

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisATProtoSessionManager> _logger;
    private readonly string _identifier;
    private readonly string _password;
    private readonly string _sessionKey;
    private readonly string _lockKey;
    private readonly JsonSerializerOptions _jsonOptions;

    private BlueskyAgent? _agent;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisATProtoSessionManager"/> class.
    /// </summary>
    /// <param name="redis">The Redis connection multiplexer.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="identifier">The ATProto account identifier (handle or DID).</param>
    /// <param name="password">The ATProto app password.</param>
    public RedisATProtoSessionManager(
        IConnectionMultiplexer redis,
        ILogger<RedisATProtoSessionManager> logger,
        string identifier,
        string password
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
    }

    /// <inheritdoc/>
    public async Task<BlueskyAgent> GetAuthenticatedAgentAsync( CancellationToken cancellationToken = default ) {
        ObjectDisposedException.ThrowIf( _disposed, this );

        // Fast path: return existing authenticated agent (no lock needed for read)
        BlueskyAgent? currentAgent = _agent;
        if (currentAgent is { IsAuthenticated: true }) {
            return currentAgent;
        }

        // Slow path: acquire lock before attempting to create/restore agent
        await _agentLock.WaitAsync( cancellationToken );
        try {
            // Double-check after acquiring lock
            currentAgent = _agent;
            if (currentAgent is { IsAuthenticated: true }) {
                return currentAgent;
            }

            // Try to restore session from Redis first
            if (await TryRestoreSessionAsync( cancellationToken )) {
                return _agent!;
            }

            // No stored session or restoration failed - perform fresh login
            return await PerformFreshLoginAsync( cancellationToken );
        } finally {
            _ = _agentLock.Release( );
        }
    }

    /// <inheritdoc/>
    public async Task<BlueskyAgent> ForceReauthenticateAsync( CancellationToken cancellationToken = default ) {
        ObjectDisposedException.ThrowIf( _disposed, this );

        LogForceReauthRequested( _identifier );

        // Clear stored credentials
        await ClearStoredCredentialsAsync( );

        // Dispose existing agent if any
        _agent?.Dispose( );
        _agent = null;

        // Perform fresh login
        return await PerformFreshLoginAsync( cancellationToken );
    }

    /// <summary>
    /// Attempts to restore a session from Redis-stored credentials.
    /// </summary>
    /// <returns>True if session was successfully restored, false otherwise.</returns>
    private async Task<bool> TryRestoreSessionAsync( CancellationToken cancellationToken ) {
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

            // Create a new agent and attempt session restoration
            BlueskyAgent agent = new( );
            SubscribeToAgentEvents( agent );

            // Parse authentication type
            if (!Enum.TryParse<AuthenticationType>( credentials.AuthenticationType, out AuthenticationType authType )) {
                authType = AuthenticationType.UsernamePassword;
            }

            // Create credential for session restoration
            // For username/password auth, we don't have DPoP fields
            AtProtoCredential restoredCredential = AtProtoCredential.Create(
                service: new Uri( credentials.Service ),
                authenticationType: authType,
                refreshToken: credentials.RefreshToken,
                dPoPProofKey: credentials.DPoPProofKey,
                dPoPNonce: credentials.DPoPNonce
            );

            // Attempt to refresh credentials (this validates and refreshes the session)
            bool refreshResult = await agent.RefreshCredentials( restoredCredential, cancellationToken );

            if (refreshResult) {
                LogSessionRestored( _identifier );
                _agent?.Dispose( );
                _agent = agent;
                return true;
            }

            LogRestoreFailed( _identifier );
            agent.Dispose( );
            return false;
        } catch (Exception ex) {
            LogRestoreException( ex, _identifier );
            return false;
        }
    }

    /// <summary>
    /// Performs a fresh login with the stored identifier and password.
    /// Uses distributed locking to prevent concurrent login attempts.
    /// </summary>
    private async Task<BlueskyAgent> PerformFreshLoginAsync( CancellationToken cancellationToken ) {
        IDatabase db = _redis.GetDatabase( );

        // Try to acquire distributed lock
        string lockValue = Guid.NewGuid( ).ToString( );
        bool lockAcquired = await db.StringSetAsync( _lockKey, lockValue, s_lockExpiry, When.NotExists );

        if (!lockAcquired) {
            // Another instance is logging in - wait briefly and retry getting authenticated agent
            LogWaitingForLock( _identifier );
            await Task.Delay( TimeSpan.FromSeconds( 2 ), cancellationToken );

            // Try to restore from Redis (the other instance should have stored credentials)
            if (await TryRestoreSessionAsync( cancellationToken )) {
                return _agent!;
            }

            // If still no session, proceed with our own login
            LogProceedingWithLogin( _identifier );
        }

        try {
            BlueskyAgent agent = new( );
            SubscribeToAgentEvents( agent );

            LogFreshLogin( _identifier );

            AtProtoHttpResult<bool> loginResult = await agent.Login( _identifier, _password, cancellationToken: cancellationToken );

            if (!loginResult.Succeeded) {
                string errorMsg = loginResult.AtErrorDetail?.Message ?? $"HTTP {loginResult.StatusCode}";
                agent.Dispose( );
                throw new InvalidOperationException( $"Failed to authenticate to PDS: {errorMsg}" );
            }

            LogAuthenticated( _identifier );

            // Persist credentials (the Authenticated event handler will also do this, but we do it explicitly for safety)
            await PersistCredentialsFromAgentAsync( agent );

            _agent?.Dispose( );
            _agent = agent;

            return agent;
        } finally {
            // Release the lock if we acquired it
            if (lockAcquired) {
                // Only delete if we still own the lock
                RedisValue currentValue = await db.StringGetAsync( _lockKey );
                if (currentValue == lockValue) {
                    _ = await db.KeyDeleteAsync( _lockKey );
                }
            }
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
    private void OnAuthenticated( object? sender, EventArgs e ) {
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
    private void OnCredentialsUpdated( object? sender, EventArgs e ) {
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
    /// Handles the TokenRefreshFailed event - clears stored credentials and triggers re-login.
    /// </summary>
    private void OnTokenRefreshFailed( object? sender, EventArgs e ) {
        LogTokenRefreshFailed( _identifier );

        // Fire and forget - clear credentials and let next request trigger re-login
        _ = Task.Run( async ( ) => {
            try {
                await ClearStoredCredentialsAsync( );
            } catch (Exception ex) {
                LogClearAfterRefreshFailed( ex, _identifier );
            }
        } );
    }

    /// <summary>
    /// Handles the Unauthenticated event - clears stored credentials.
    /// </summary>
    private void OnUnauthenticated( object? sender, EventArgs e ) {
        LogUnauthenticated( _identifier );

        // Fire and forget - clear credentials
        _ = Task.Run( async ( ) => {
            try {
                await ClearStoredCredentialsAsync( );
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
            Handle = _identifier, // Use configured identifier (handle or DID used for login)
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

    /// <inheritdoc/>
    public void Dispose( ) {
        if (_disposed) { return; }

        _agent?.Dispose( );
        _agent = null;
        _agentLock.Dispose( );
        _disposed = true;
    }

}
