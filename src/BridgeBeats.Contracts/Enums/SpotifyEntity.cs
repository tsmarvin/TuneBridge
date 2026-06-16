namespace BridgeBeats.Contracts.Enums;

/// <summary>
/// The kind of Spotify item a parsed link or id refers to. Each value is a single, mutually
/// exclusive choice used by the Spotify link parser to branch on item type.
/// </summary>
/// <remarks>
/// This is a plain discriminator, not a bit flag. The numeric values happen to be powers of two
/// with a gap at 3, but the enum is not marked <c>[Flags]</c> and the values are never combined;
/// compare a single value with <c>==</c> or <c>switch</c>, do not test bits.
/// </remarks>
public enum SpotifyEntity {

    /// <summary>
    /// The item type could not be determined from the link or id.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The item is a Spotify album. Albums are matched across platforms by UPC when available.
    /// </summary>
    Album = 1,

    /// <summary>
    /// The item is a Spotify artist.
    /// </summary>
    Artist = 2,

    /// <summary>
    /// The item is a Spotify track. Tracks are matched across platforms by ISRC when available.
    /// </summary>
    Track = 4,

    /// <summary>
    /// The item is a Spotify playlist. Defined but not resolved for cross-platform matching.
    /// </summary>
    Playlist = 8,

    /// <summary>
    /// The item is a Spotify pre-release (an announced item not yet generally available). Parsed
    /// from links but not yet resolved.
    /// </summary>
    PreRelease = 16
}
