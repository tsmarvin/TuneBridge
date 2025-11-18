using System.ComponentModel.DataAnnotations;

namespace TuneBridge.JetStreamMonitor.Infrastructure;

/// <summary>
/// Represents a music link detected from the Jetstream.
/// </summary>
public class DetectedMusicLink {

    /// <summary>
    /// Auto-incrementing primary key.
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The music link URL (unique).
    /// </summary>
    [Required]
    [MaxLength( 2048 )]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The provider name (Apple Music, Spotify, Tidal).
    /// </summary>
    [Required]
    [MaxLength( 50 )]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The AT-URI of the post where this link was detected.
    /// </summary>
    [Required]
    [MaxLength( 512 )]
    public string PostUri { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the link was first detected.
    /// </summary>
    public DateTimeOffset FirstDetectedAt { get; set; }

    /// <summary>
    /// Timestamp when the link was last seen.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Number of times this link has been seen across different posts.
    /// </summary>
    public int SeenCount { get; set; }
}
