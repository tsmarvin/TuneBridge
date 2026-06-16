using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Application user entity extending the ASP.NET Core Identity user with API-key,
/// rate-limiting, Apple Music, and ATProto identity fields.
/// </summary>
/// <remarks>
/// <para>
/// The four credential fields — <see cref="AppleMusicUserToken"/>, <see cref="AtProtoAccessToken"/>,
/// <see cref="AtProtoRefreshToken"/>, and <see cref="AtProtoDPoPKey"/> — are encrypted at rest via
/// an EF Core value converter registered in <see cref="ApplicationDbContext.OnModelCreating"/>. The
/// converter uses an <see cref="Microsoft.AspNetCore.DataProtection.IDataProtector"/> created with
/// purpose <c>BridgeBeats.ApplicationUser.Tokens.v1</c>; values are stored as opaque ciphertext in
/// the <c>TEXT</c> column (schema-transparent: column type stays TEXT, no DDL change in migrations).
/// </para>
/// <para>
/// <see cref="ApiKeyHash"/>, <see cref="AtProtoDid"/>, and <see cref="AtProtoHandle"/> are stored
/// in plaintext because they are uniquely indexed and must support exact-match lookups; encrypting
/// them would break those indexes. <c>NormalizedEmail</c> is also plaintext for the same reason.
/// </para>
/// <para>
/// <c>AddPersonalDataProtection</c> is still registered so that <c>IPersonalDataProtector</c>
/// is available for the ATProto OAuth service (<see cref="ATProtoOAuthService"/>) which uses it to
/// protect transient OAuth-state fields. The <c>[ProtectedPersonalData]</c> attribute has been
/// removed from the four credential fields — the EF value converter is the encryption mechanism.
/// </para>
/// </remarks>
public class ApplicationUser : IdentityUser {
    /// <summary>
    /// Gets or sets the HMAC-SHA256 hash of the user's API key. A unique index over this column
    /// enables resolving a presented key to its owning user. Stored in plaintext for index support.
    /// </summary>
    public string? ApiKeyHash { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which the user was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the number of requests counted in the current rate-limit window.</summary>
    public int RequestCount { get; set; }

    /// <summary>Gets or sets the UTC start time of the current rate-limit window, or <see langword="null"/> if no window is active.</summary>
    public DateTime? RateLimitWindowStart { get; set; }

    /// <summary>
    /// Gets or sets the Apple Music user token. Encrypted at rest via the EF value converter in
    /// <see cref="ApplicationDbContext.OnModelCreating"/> (purpose <c>BridgeBeats.ApplicationUser.Tokens.v1</c>).
    /// </summary>
    public string? AppleMusicUserToken { get; set; }

    /// <summary>Gets or sets the UTC expiry of the Apple Music user token, or <see langword="null"/> if not set.</summary>
    public DateTime? AppleMusicTokenExpiration { get; set; }

    /// <summary>
    /// Gets or sets the user's ATProto decentralized identifier (DID), the unique identifier for the
    /// user's ATProto account. Stored in plaintext; a unique filtered index enforces at most one
    /// account per non-null DID.
    /// </summary>
    public string? AtProtoDid { get; set; }

    /// <summary>Gets or sets the user's ATProto handle (for example, @user.bsky.social). Stored in plaintext for lookup support.</summary>
    public string? AtProtoHandle { get; set; }

    /// <summary>
    /// Gets or sets the ATProto OAuth access token used to access the user's PDS. Encrypted at rest
    /// via the EF value converter in <see cref="ApplicationDbContext.OnModelCreating"/>.
    /// </summary>
    public string? AtProtoAccessToken { get; set; }

    /// <summary>
    /// Gets or sets the ATProto OAuth refresh token used to obtain new access tokens. Encrypted at
    /// rest via the EF value converter in <see cref="ApplicationDbContext.OnModelCreating"/>.
    /// </summary>
    public string? AtProtoRefreshToken { get; set; }

    /// <summary>
    /// Gets or sets the ATProto DPoP (Demonstration of Proof-of-Possession) private key as a JWK
    /// string, used to sign requests and prove possession of the OAuth tokens bound to this client.
    /// Encrypted at rest via the EF value converter in <see cref="ApplicationDbContext.OnModelCreating"/>.
    /// </summary>
    public string? AtProtoDPoPKey { get; set; }

    /// <summary>Gets or sets the UTC expiry of the ATProto access token, or <see langword="null"/> if not set.</summary>
    public DateTime? AtProtoTokenExpiration { get; set; }
}
