using Microsoft.AspNetCore.Identity;

namespace TuneBridge.Domain.Models;

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
    /// Spotify OAuth access token for user-specific API access.
    /// </summary>
    public string? SpotifyAccessToken { get; set; }

    /// <summary>
    /// Spotify OAuth refresh token for renewing access.
    /// </summary>
    public string? SpotifyRefreshToken { get; set; }

    /// <summary>
    /// Timestamp when the Spotify access token expires.
    /// </summary>
    public DateTime? SpotifyTokenExpiry { get; set; }
}
