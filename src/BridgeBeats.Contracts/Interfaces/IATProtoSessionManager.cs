using idunno.Bluesky;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Supplies an authenticated Bluesky agent for talking to the AT Protocol PDS, managing
/// the underlying session so callers do not have to re-authenticate per request.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>RedisATProtoSessionManager</c>
/// (<c>Infrastructure/Storage/RedisATProtoSessionManager.cs</c>), which caches the session
/// in Redis. Centralizing session management prevents excessive PDS API calls
/// (createSession, getSession, refreshSession) across multiple worker instances; credentials
/// are persisted to Redis and restored on startup. The implementation is disposable even
/// though this contract is not.
/// </remarks>
public interface IATProtoSessionManager {

    /// <summary>
    /// Returns an authenticated <see cref="BlueskyAgent"/>, restoring a cached session from
    /// Redis on the first call and refreshing it when needed, authenticating on demand when no
    /// usable session is available.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is a ready-to-use authenticated agent.</returns>
    /// <exception cref="InvalidOperationException">Thrown when authentication fails and cannot be recovered.</exception>
    Task<BlueskyAgent> GetAuthenticatedAgentAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Discards any cached session, clears the Redis-stored credentials, and authenticates again
    /// from scratch with the stored password, returning a freshly authenticated agent. Use when
    /// the current session is known to be invalid (for example, after repeated failures).
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is a newly authenticated agent.</returns>
    /// <exception cref="InvalidOperationException">Thrown when re-authentication fails.</exception>
    Task<BlueskyAgent> ForceReauthenticateAsync( CancellationToken cancellationToken = default );

}
