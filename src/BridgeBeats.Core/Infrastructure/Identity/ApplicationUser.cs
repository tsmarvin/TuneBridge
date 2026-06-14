using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Application user entity extending the ASP.NET Core Identity user with API-key,
/// rate-limiting, Apple Music, and ATProto identity fields.
/// </summary>
/// <remarks>
/// Fields marked <see cref="ProtectedPersonalDataAttribute"/> are stored as <b>plaintext</b>
/// today: <c>ProtectPersonalData</c> is never enabled in this application's Identity
/// configuration, so the <c>[ProtectedPersonalData]</c> attribute is present but inert.
/// Fields such as <see cref="ApiKeyHash"/>, <see cref="AtProtoDid"/>, and
/// <see cref="AtProtoHandle"/> are also stored unencrypted to support indexing and lookups.
/// </remarks>
public class ApplicationUser : IdentityUser {
    /// <summary>
    /// Gets or sets the HMAC-SHA256 hash of the user's API key. A unique index over this column
    /// enables resolving a presented key to its owning user.
    /// </summary>
    public string? ApiKeyHash { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which the user was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the number of requests counted in the current rate-limit window.</summary>
    public int RequestCount { get; set; }

    /// <summary>Gets or sets the UTC start time of the current rate-limit window, or <see langword="null"/> if no window is active.</summary>
    public DateTime? RateLimitWindowStart { get; set; }

    /// <summary>Gets or sets the Apple Music user token. Stored plaintext at rest; <c>[ProtectedPersonalData]</c> is inert because <c>ProtectPersonalData</c> is not enabled.</summary>
    [ProtectedPersonalData]
    public string? AppleMusicUserToken { get; set; }

    /// <summary>Gets or sets the UTC expiry of the Apple Music user token, or <see langword="null"/> if not set.</summary>
    public DateTime? AppleMusicTokenExpiration { get; set; }

    /// <summary>
    /// Gets or sets the user's ATProto decentralized identifier (DID), the unique identifier for the
    /// user's ATProto account. Stored in plaintext; a unique filtered index enforces at most one
    /// account per non-null DID.
    /// </summary>
    public string? AtProtoDid { get; set; }

    /// <summary>Gets or sets the user's ATProto handle (for example, @user.bsky.social). Stored in plaintext.</summary>
    public string? AtProtoHandle { get; set; }

    /// <summary>Gets or sets the ATProto OAuth access token used to access the user's PDS. Stored plaintext at rest; <c>[ProtectedPersonalData]</c> is inert because <c>ProtectPersonalData</c> is not enabled.</summary>
    [ProtectedPersonalData]
    public string? AtProtoAccessToken { get; set; }

    /// <summary>Gets or sets the ATProto OAuth refresh token used to obtain new access tokens. Stored plaintext at rest; <c>[ProtectedPersonalData]</c> is inert because <c>ProtectPersonalData</c> is not enabled.</summary>
    [ProtectedPersonalData]
    public string? AtProtoRefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the ATProto DPoP (Demonstration of Proof-of-Possession) private key as a JWK
    /// string, used to sign requests and prove possession of the OAuth tokens bound to this client.
    /// Stored plaintext at rest; <c>[ProtectedPersonalData]</c> is inert because
    /// <c>ProtectPersonalData</c> is not enabled.
    /// </summary>
    [ProtectedPersonalData]
    public string? AtProtoDPoPKey { get; set; }

    /// <summary>Gets or sets the UTC expiry of the ATProto access token, or <see langword="null"/> if not set.</summary>
    public DateTime? AtProtoTokenExpiration { get; set; }
}
