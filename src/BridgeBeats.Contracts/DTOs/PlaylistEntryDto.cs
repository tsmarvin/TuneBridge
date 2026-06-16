namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Database projection of a stored playlist of music cards (tracks or albums), returned by the
/// playlist service. A mutable transfer object with settable properties; the card id and rkey lists
/// are carried as comma-delimited strings rather than collections, and a playlist holds up to 20
/// cards.
/// </summary>
/// <remarks>
/// This DTO abstracts the persistence-layer entity (<c>PlaylistEntry</c>) so that Contracts stays
/// free of Entity Framework dependencies; infrastructure code maps between this DTO and its EF
/// entity.
/// </remarks>
public sealed class PlaylistEntryDto {

    /// <summary>
    /// The playlist's identifier, generated deterministically from the ordered card IDs. Defaults to
    /// an empty string until populated.
    /// </summary>
    public string PlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// The owning user's identifier, or <see langword="null"/> for an anonymous playlist with no
    /// owner.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The user-defined playlist title, or <see langword="null"/> when unset.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// The user-defined playlist description, or <see langword="null"/> when unset.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The playlist's card identifiers (rkeys) as a single comma-delimited string, paired
    /// positionally with <see cref="CardRkeys"/>. Order is preserved; up to 20 items. Defaults to an
    /// empty string.
    /// </summary>
    public string CardIds { get; set; } = string.Empty;

    /// <summary>
    /// The playlist's original card record keys as a single comma-delimited string, paired
    /// positionally with <see cref="CardIds"/>. Used to regenerate card content for old playlists.
    /// Order is preserved and matches <see cref="CardIds"/>; up to 20 items. Defaults to an empty
    /// string.
    /// </summary>
    public string CardRkeys { get; set; } = string.Empty;

    /// <summary>
    /// When the playlist was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the playlist expires, or <see langword="null"/> when it never expires. Logged-in users'
    /// playlists never expire; anonymous playlists expire 14 days after creation.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }
}
