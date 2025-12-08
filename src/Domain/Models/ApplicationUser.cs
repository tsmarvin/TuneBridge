using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Domain.Models;

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
    public string? AppleMusicUserToken { get; set; }

    /// <summary>
    /// Expiration time of the Apple Music user token.
    /// </summary>
    public DateTime? AppleMusicTokenExpiration { get; set; }
}
