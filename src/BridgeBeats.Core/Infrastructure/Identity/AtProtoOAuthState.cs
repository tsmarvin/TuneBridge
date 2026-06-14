using System.ComponentModel.DataAnnotations;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Transient per-login row holding the state needed to complete an ATProto OAuth exchange.
/// </summary>
/// <remarks>
/// A row is created when authorization starts and removed once the exchange completes or expires
/// (default lifetime five minutes). It persists the state value and PKCE code verifier between the
/// authorization request and the callback. The size limits declared by these data annotations are
/// also applied via the Fluent API in <see cref="ApplicationDbContext"/>, where the Fluent
/// configuration is authoritative for the database schema. <see cref="CodeVerifier"/> and
/// <see cref="DPoPKeyJwk"/> are stored encrypted, so their persisted lengths are ciphertext lengths.
/// </remarks>
public class AtProtoOAuthState {
    /// <summary>Gets or sets the opaque OAuth state value. Serves as the primary key and CSRF/replay binding.</summary>
    [Key]
    [MaxLength( 128 )]
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets the PKCE code verifier used to prove the callback is from the same client that
    /// started the flow. Stored encrypted; the persisted value is ciphertext.
    /// </summary>
    [Required]
    [MaxLength( 512 )]
    public required string CodeVerifier { get; set; }

    /// <summary>Gets or sets the ATProto handle the user is authenticating with.</summary>
    [Required]
    [MaxLength( 256 )]
    public required string Handle { get; set; }

    /// <summary>Gets or sets the resolved DID for the handle, or <see langword="null"/> if resolution has not succeeded before redirect.</summary>
    [MaxLength( 128 )]
    public string? Did { get; set; }

    /// <summary>Gets or sets the user's Personal Data Server (PDS) URI, resolved during authorization, or <see langword="null"/> if not set.</summary>
    [MaxLength( 512 )]
    public string? PdsUri { get; set; }

    /// <summary>Gets or sets the authorization server URI, resolved during authorization and used to validate the issuer on callback.</summary>
    [MaxLength( 512 )]
    public string? AuthorizationServerUri { get; set; }

    /// <summary>
    /// Gets or sets the DPoP private key as a JWK string, generated for this OAuth session. Stored
    /// encrypted; the persisted value is ciphertext.
    /// </summary>
    [Required]
    public required string DPoPKeyJwk { get; set; }

    /// <summary>Gets or sets the UTC timestamp at which this state row was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC expiry of this state row. Defaults to five minutes after creation.</summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes( 5 );
}
