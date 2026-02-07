using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Represents an application user with Identity functionality.
/// Minimal user information stored - only unique ID and authentication means.
/// </summary>
public class ApplicationUser : IdentityUser {
    /// <summary>
    /// Hashed API key for authenticating API requests.
    /// Stored as a salted hash for security.
    /// </summary>
    public string? ApiKeyHash { get; set; }

    /// <summary>
    /// Timestamp of when the user was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Count of requests made in the current rate limit window.
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Start of the current rate limit window.
    /// </summary>
    public DateTime? RateLimitWindowStart { get; set; }

    /// <summary>
    /// Apple Music user token for accessing the user's library and playlists.
    /// </summary>
    [ProtectedPersonalData]
    public string? AppleMusicUserToken { get; set; }

    /// <summary>
    /// Expiration time of the Apple Music user token.
    /// </summary>
    public DateTime? AppleMusicTokenExpiration { get; set; }

    /// <summary>
    /// ATProto Decentralized Identifier (DID) for the user.
    /// This is the unique identifier for the user's ATProto account.
    /// </summary>
    public string? AtProtoDid { get; set; }

    /// <summary>
    /// ATProto handle (e.g., @user.bsky.social) for the user.
    /// </summary>
    public string? AtProtoHandle { get; set; }

    /// <summary>
    /// ATProto OAuth access token for accessing the user's PDS.
    /// </summary>
    [ProtectedPersonalData]
    public string? AtProtoAccessToken { get; set; }

    /// <summary>
    /// ATProto OAuth refresh token for obtaining new access tokens.
    /// </summary>
    [ProtectedPersonalData]
    public string? AtProtoRefreshToken { get; set; }

    /// <summary>
    /// ATProto DPoP (Demonstration of Proof-of-Possession) private key in JWK format.
    /// Used to sign requests and prove ownership of OAuth tokens.
    /// Encrypted at rest via the Data Protection personal data protector.
    /// </summary>
    [ProtectedPersonalData]
    public string? AtProtoDPoPKey { get; set; }

    /// <summary>
    /// Expiration time of the ATProto OAuth access token.
    /// </summary>
    public DateTime? AtProtoTokenExpiration { get; set; }
}
