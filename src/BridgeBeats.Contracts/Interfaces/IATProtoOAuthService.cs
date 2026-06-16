using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Drives the AT Protocol (Bluesky) OAuth authorization-code flow: starting an
/// authorization, exchanging the returned code for tokens, refreshing tokens, and
/// housekeeping of expired in-flight authorization state.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>ATProtoOAuthService</c>
/// (<c>Infrastructure/Identity/ATProtoOAuthService.cs</c>). The flow uses DPoP
/// (Demonstrating Proof of Possession): the caller supplies a DPoP proof key (as a
/// JWK) when refreshing tokens.
/// </remarks>
public interface IATProtoOAuthService {

    /// <summary>
    /// Begins an authorization-code flow for the given Bluesky handle. Resolves the handle to a
    /// DID, discovers the authorization server, and produces the authorization URL the user must
    /// visit together with the opaque <c>state</c> value that ties the eventual callback back to
    /// this request.
    /// </summary>
    /// <param name="handle">The Bluesky handle (for example <c>alice.bsky.social</c>) to authorize.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is a tuple of the authorization URL to redirect the user to and
    /// the <c>state</c> value that must be presented back to
    /// <see cref="CompleteAuthorizationAsync"/> to complete the exchange.
    /// </returns>
    Task<(Uri AuthorizationUrl, string State)> StartAuthorizationAsync(
        string handle,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Completes the authorization-code exchange after the user returns from the identity
    /// provider, trading the callback parameters for live tokens.
    /// </summary>
    /// <param name="state">The <c>state</c> value originally returned by <see cref="StartAuthorizationAsync"/>; correlates the callback to the pending request.</param>
    /// <param name="code">The authorization code returned on the OAuth callback.</param>
    /// <param name="iss">The issuer identifier returned on the OAuth callback, used for verification.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the completed <see cref="ATProtoOAuthResult"/> carrying the
    /// access and refresh tokens, DID, handle, DPoP key, and token expiration.
    /// </returns>
    Task<ATProtoOAuthResult> CompleteAuthorizationAsync(
        string state,
        string code,
        string iss,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Exchanges a stored refresh token for a fresh set of tokens, re-proving possession of
    /// the DPoP key.
    /// </summary>
    /// <param name="did">The DID (decentralized identifier) the tokens belong to.</param>
    /// <param name="refreshToken">The refresh token previously issued for this DID.</param>
    /// <param name="dpoPKeyJwk">The DPoP proof key, serialized as a JWK, that authorized the original tokens.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is a refreshed <see cref="ATProtoOAuthResult"/>, or <see langword="null"/>
    /// when the refresh token is no longer valid and no new tokens could be obtained.
    /// </returns>
    Task<ATProtoOAuthResult?> RefreshTokensAsync(
        string did,
        string refreshToken,
        string dpoPKeyJwk,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reports whether a token with the given expiration is still considered valid at the
    /// time of the call. Uses a 30-second safety margin.
    /// </summary>
    /// <param name="tokenExpiration">The token's expiration instant, or <see langword="null"/> when no expiration is known.</param>
    /// <returns><see langword="true"/> if the token is still valid; otherwise <see langword="false"/>.</returns>
    bool IsTokenValid( DateTime? tokenExpiration );

    /// <summary>
    /// Removes expired in-flight authorization state (pending <c>state</c> entries that were
    /// never completed) so it does not accumulate.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the number of expired authorization-state entries removed.</returns>
    Task<int> CleanupExpiredStatesAsync( CancellationToken cancellationToken = default );
}
