using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for managing ATProto OAuth authentication flows.
/// </summary>
public interface IATProtoOAuthService {

    /// <summary>
    /// Starts an OAuth authorization flow for the given ATProto handle.
    /// Resolves the handle to a DID, discovers the authorization server,
    /// and returns the authorization URL to redirect the user to.
    /// </summary>
    /// <param name="handle">The ATProto handle (e.g., "user.bsky.social").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The authorization URL to redirect the user to, and the state for correlation.</returns>
    Task<(Uri AuthorizationUrl, string State)> StartAuthorizationAsync(
        string handle,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Completes the OAuth authorization flow by exchanging the authorization code for tokens.
    /// </summary>
    /// <param name="state">The state parameter from the callback.</param>
    /// <param name="code">The authorization code from the callback.</param>
    /// <param name="iss">The issuer from the callback (for verification).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OAuth result containing tokens and user information.</returns>
    Task<ATProtoOAuthResult> CompleteAuthorizationAsync(
        string state,
        string code,
        string iss,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Refreshes the OAuth tokens for a user using stored credentials.
    /// </summary>
    /// <param name="did">The user's ATProto DID.</param>
    /// <param name="refreshToken">The current refresh token.</param>
    /// <param name="dpoPKeyJwk">The DPoP private key in JWK format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refreshed OAuth result, or null if refresh failed.</returns>
    Task<ATProtoOAuthResult?> RefreshTokensAsync(
        string did,
        string refreshToken,
        string dpoPKeyJwk,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Checks if the given token expiration time indicates valid tokens (not expired).
    /// Uses a 30-second safety margin.
    /// </summary>
    /// <param name="tokenExpiration">The token expiration time.</param>
    /// <returns>True if tokens are valid, false otherwise.</returns>
    bool IsTokenValid( DateTime? tokenExpiration );

    /// <summary>
    /// Cleans up expired OAuth state entries from the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of expired entries removed.</returns>
    Task<int> CleanupExpiredStatesAsync( CancellationToken cancellationToken = default );
}
