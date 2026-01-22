using System.ComponentModel.DataAnnotations;

namespace BridgeBeats.Infrastructure.Identity;

/// <summary>
/// Stores OAuth state during the ATProto OAuth flow.
/// Used to persist state and PKCE code verifier between authorization request and callback.
/// Records are short-lived (5 minutes) and deleted after successful callback.
/// </summary>
public class AtProtoOAuthState {
    /// <summary>
    /// Primary key - the OAuth state parameter value.
    /// </summary>
    [Key]
    [MaxLength( 128 )]
    public required string State { get; set; }

    /// <summary>
    /// PKCE code verifier used to prove the callback is from the same client that started the flow.
    /// </summary>
    [Required]
    [MaxLength( 128 )]
    public required string CodeVerifier { get; set; }

    /// <summary>
    /// The ATProto handle the user is attempting to authenticate with.
    /// </summary>
    [Required]
    [MaxLength( 256 )]
    public required string Handle { get; set; }

    /// <summary>
    /// The resolved DID for the handle (if resolution succeeded before redirect).
    /// </summary>
    [MaxLength( 128 )]
    public string? Did { get; set; }

    /// <summary>
    /// The user's PDS URI (resolved during authorization).
    /// </summary>
    [MaxLength( 512 )]
    public string? PdsUri { get; set; }

    /// <summary>
    /// The authorization server URI (resolved during authorization).
    /// </summary>
    [MaxLength( 512 )]
    public string? AuthorizationServerUri { get; set; }

    /// <summary>
    /// The DPoP private key in JWK format, generated for this OAuth session.
    /// </summary>
    [Required]
    public required string DPoPKeyJwk { get; set; }

    /// <summary>
    /// When this OAuth state was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this OAuth state expires (default 5 minutes from creation).
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes( 5 );
}
