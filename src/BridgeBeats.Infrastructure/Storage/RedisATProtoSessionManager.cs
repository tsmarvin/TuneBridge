using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using idunno.AtProto;
using idunno.AtProto.Authentication;
using idunno.Bluesky;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Storage;

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
public sealed class RedisATProtoSessionManager : IATProtoSessionManager, IDisposable {

    private const string SessionKeyPrefix = "atproto:session:";
    private const string LockKeyPrefix = "atproto:auth:lock:";
    private static readonly TimeSpan s_lockExpiry = TimeSpan.FromSeconds( 30 );

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

        // Fast path: return existing authenticated agent
        if (_agent is { IsAuthenticated: true }) {
            return _agent;
        }

        // Try to restore session from Redis first
        if (await TryRestoreSessionAsync( cancellationToken )) {
            return _agent!;
        }

        // No stored session or restoration failed - perform fresh login
        return await PerformFreshLoginAsync( cancellationToken );
    }

    /// <inheritdoc/>
    public async Task<BlueskyAgent> ForceReauthenticateAsync( CancellationToken cancellationToken = default ) {
        ObjectDisposedException.ThrowIf( _disposed, this );

        _logger.LogWarning( "Force re-authentication requested for {Identifier}", _identifier );

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
                _logger.LogDebug( "No stored credentials found in Redis for {Identifier}", _identifier );
                return false;
            }

            ATProtoPersistedCredentials? credentials = JsonSerializer.Deserialize<ATProtoPersistedCredentials>(
                storedCredentials.ToString( ),
                _jsonOptions
            );

            if (credentials is null) {
                _logger.LogWarning( "Failed to deserialize stored credentials for {Identifier}", _identifier );
                return false;
            }

            _logger.LogInformation(
                "Attempting to restore session for {Identifier} (persisted at {PersistedAt})",
                _identifier,
                credentials.PersistedAt
            );

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
                _logger.LogInformation( "Successfully restored session for {Identifier}", _identifier );
                _agent?.Dispose( );
                _agent = agent;
                return true;
            }

            _logger.LogWarning( "Failed to restore session for {Identifier} - refresh returned false", _identifier );
            agent.Dispose( );
            return false;
        } catch (Exception ex) {
            _logger.LogWarning( ex, "Exception while restoring session for {Identifier}", _identifier );
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
            _logger.LogDebug( "Another instance is performing login for {Identifier}, waiting...", _identifier );
            await Task.Delay( TimeSpan.FromSeconds( 2 ), cancellationToken );

            // Try to restore from Redis (the other instance should have stored credentials)
            if (await TryRestoreSessionAsync( cancellationToken )) {
                return _agent!;
            }

            // If still no session, proceed with our own login
            _logger.LogWarning( "Still no session after waiting - proceeding with login for {Identifier}", _identifier );
        }

        try {
            BlueskyAgent agent = new( );
            SubscribeToAgentEvents( agent );

            _logger.LogInformation( "Performing fresh login for {Identifier}", _identifier );

            AtProtoHttpResult<bool> loginResult = await agent.Login( _identifier, _password );

            if (!loginResult.Succeeded) {
                string errorMsg = loginResult.AtErrorDetail?.Message ?? $"HTTP {loginResult.StatusCode}";
                agent.Dispose( );
                throw new InvalidOperationException( $"Failed to authenticate to PDS: {errorMsg}" );
            }

            _logger.LogInformation( "Successfully authenticated to PDS for {Identifier}", _identifier );

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
        _logger.LogDebug( "Agent authenticated event for {Identifier}", _identifier );

        if (sender is BlueskyAgent agent) {
            // Fire and forget - don't block the event
            _ = Task.Run( async ( ) => {
                try {
                    await PersistCredentialsFromAgentAsync( agent );
                } catch (Exception ex) {
                    _logger.LogError( ex, "Failed to persist credentials after authentication for {Identifier}", _identifier );
                }
            } );
        }
    }

    /// <summary>
    /// Handles the CredentialsUpdated event - persists refreshed credentials.
    /// </summary>
    private void OnCredentialsUpdated( object? sender, EventArgs e ) {
        _logger.LogDebug( "Agent credentials updated event for {Identifier}", _identifier );

        if (sender is BlueskyAgent agent) {
            // Fire and forget - don't block the event
            _ = Task.Run( async ( ) => {
                try {
                    await PersistCredentialsFromAgentAsync( agent );
                } catch (Exception ex) {
                    _logger.LogError( ex, "Failed to persist updated credentials for {Identifier}", _identifier );
                }
            } );
        }
    }

    /// <summary>
    /// Handles the TokenRefreshFailed event - clears stored credentials and triggers re-login.
    /// </summary>
    private void OnTokenRefreshFailed( object? sender, EventArgs e ) {
        _logger.LogWarning( "Token refresh failed for {Identifier} - clearing stored credentials", _identifier );

        // Fire and forget - clear credentials and let next request trigger re-login
        _ = Task.Run( async ( ) => {
            try {
                await ClearStoredCredentialsAsync( );
            } catch (Exception ex) {
                _logger.LogError( ex, "Failed to clear credentials after token refresh failure for {Identifier}", _identifier );
            }
        } );
    }

    /// <summary>
    /// Handles the Unauthenticated event - clears stored credentials.
    /// </summary>
    private void OnUnauthenticated( object? sender, EventArgs e ) {
        _logger.LogInformation( "Agent unauthenticated event for {Identifier}", _identifier );

        // Fire and forget - clear credentials
        _ = Task.Run( async ( ) => {
            try {
                await ClearStoredCredentialsAsync( );
            } catch (Exception ex) {
                _logger.LogError( ex, "Failed to clear credentials after unauthenticated event for {Identifier}", _identifier );
            }
        } );
    }

    /// <summary>
    /// Persists the agent's current credentials to Redis.
    /// </summary>
    private async Task PersistCredentialsFromAgentAsync( BlueskyAgent agent ) {
        if (!agent.IsAuthenticated || agent.Credentials is null) {
            _logger.LogDebug( "Cannot persist credentials - agent not authenticated" );
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

        _logger.LogDebug( "Persisted credentials to Redis for {Identifier}", _identifier );
    }

    /// <summary>
    /// Clears stored credentials from Redis.
    /// </summary>
    private async Task ClearStoredCredentialsAsync( ) {
        IDatabase db = _redis.GetDatabase( );
        _ = await db.KeyDeleteAsync( _sessionKey );
        _logger.LogDebug( "Cleared stored credentials from Redis for {Identifier}", _identifier );
    }

    /// <inheritdoc/>
    public void Dispose( ) {
        if (_disposed) { return; }

        _agent?.Dispose( );
        _agent = null;
        _disposed = true;
    }

}
