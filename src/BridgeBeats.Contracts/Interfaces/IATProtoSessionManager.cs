using idunno.Bluesky;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Manages ATProto authentication sessions with shared state across service instances.
/// Handles token acquisition, refresh, persistence to Redis, and re-authentication on failure.
/// </summary>
/// <remarks>
/// This service centralizes session management to prevent excessive PDS API calls
/// (refreshSession, getSession, createSession) across multiple worker instances.
/// Credentials are persisted to Redis and restored on startup using the idunno.Bluesky
/// session restoration pattern with <c>AtProtoCredential.Create()</c> and <c>RefreshCredentials()</c>.
/// </remarks>
public interface IATProtoSessionManager {

    /// <summary>
    /// Gets an authenticated BlueskyAgent ready for use.
    /// Handles session restoration from Redis on first call, and refreshes if needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>An authenticated BlueskyAgent instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when authentication fails and cannot be recovered.</exception>
    Task<BlueskyAgent> GetAuthenticatedAgentAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Forces a fresh login, invalidating any cached session.
    /// Use when the current session is known to be invalid (e.g., after repeated failures).
    /// Clears Redis-stored credentials and performs a new login with the stored password.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>An authenticated BlueskyAgent instance from the fresh login.</returns>
    /// <exception cref="InvalidOperationException">Thrown when re-authentication fails.</exception>
    Task<BlueskyAgent> ForceReauthenticateAsync( CancellationToken cancellationToken = default );

}
