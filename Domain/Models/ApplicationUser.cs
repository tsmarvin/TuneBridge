using Microsoft.AspNetCore.Identity;

namespace TuneBridge.Domain.Models;

/// <summary>
/// Represents an application user with Identity functionality.
/// Extends IdentityUser to add custom properties for user authentication and tracking.
/// </summary>
public class ApplicationUser : IdentityUser {
    /// <summary>
    /// API key for authenticating API requests.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Timestamp of when the user was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of the last API request made by this user.
    /// </summary>
    public DateTime? LastRequestAt { get; set; }

    /// <summary>
    /// Count of requests made in the current rate limit window.
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Start of the current rate limit window.
    /// </summary>
    public DateTime? RateLimitWindowStart { get; set; }
}
