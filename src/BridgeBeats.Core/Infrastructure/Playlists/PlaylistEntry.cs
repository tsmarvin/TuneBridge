namespace BridgeBeats.Core.Infrastructure.Playlists;

/// <summary>
/// Entity Framework-mapped entity for a saved playlist of media-link cards, persisted to the
/// application database. A playlist holds up to 20 cards and may belong to a user or be
/// anonymous; anonymous playlists carry an expiry, claimed playlists do not.
/// </summary>
public class PlaylistEntry {

    /// <summary>
    /// Primary key. A deterministic identifier derived from the ordered card identifiers, so the
    /// same set of cards in the same order always yields the same playlist id.
    /// </summary>
    public string PlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the owning user, or <see langword="null"/> for an anonymous playlist.
    /// Claiming an anonymous playlist by setting this value clears its expiry.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>Optional user-defined title for the playlist.</summary>
    public string? Title { get; set; }

    /// <summary>Optional user-defined description for the playlist.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Comma-separated list of card identifiers in the playlist, at most 20 items, order
    /// preserved. Drives the deterministic <see cref="PlaylistId"/>.
    /// </summary>
    public string CardIds { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of original rkey values corresponding to <see cref="CardIds"/>, parallel
    /// to it with the same count and order. Used to regenerate card content for old playlists.
    /// </summary>
    public string CardRkeys { get; set; } = string.Empty;

    /// <summary>The timestamp when this playlist was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The timestamp when this playlist expires, or <see langword="null"/> when it never expires
    /// (for playlists owned by a logged-in user). Anonymous playlists expire 14 days after creation.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
