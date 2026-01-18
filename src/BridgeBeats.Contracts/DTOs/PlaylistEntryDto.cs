namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Data transfer object representing a playlist of music cards (tracks or albums).
/// Each playlist contains up to 20 card IDs (rkeys) that can be resolved to MediaLinkResults.
/// </summary>
/// <remarks>
/// This DTO abstracts the persistence layer entity (PlaylistEntry) to keep Contracts
/// free of Entity Framework dependencies. Infrastructure implementations should map
/// between this DTO and their EF entities.
/// </remarks>
public sealed class PlaylistEntryDto {

    /// <summary>
    /// The unique identifier for the playlist.
    /// Generated deterministically based on the ordered card IDs.
    /// </summary>
    public string PlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Optional user ID if the playlist was created by a logged-in user.
    /// Null for anonymous playlists.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Optional user-defined title for the playlist.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional user-defined description for the playlist.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Comma-separated list of card IDs (rkeys) in the playlist.
    /// Maximum 20 items. Order is preserved.
    /// </summary>
    public string CardIds { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of original rkey values corresponding to CardIds.
    /// Used to regenerate card content quickly for old playlists.
    /// Maximum 20 items. Order is preserved and matches CardIds order.
    /// </summary>
    public string CardRkeys { get; set; } = string.Empty;

    /// <summary>
    /// The timestamp when this playlist was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The timestamp when this playlist expires (nullable).
    /// Null = never expires (for logged-in users)
    /// 2 weeks from creation for anonymous users
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
