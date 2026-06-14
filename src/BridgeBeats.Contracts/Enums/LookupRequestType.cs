namespace BridgeBeats.Contracts.Enums;

/// <summary>
/// The strategy used to look up a piece of media. Tags each provider API request and drives the
/// saga's <c>LookupType</c>.
/// </summary>
/// <remarks>
/// There are more lookup strategies here than there are worker request records, so the mapping is
/// not one-to-one: several strategies are served by the same request shape on the worker side.
/// </remarks>
public enum LookupRequestType {

    /// <summary>
    /// Look up a track by its ISRC (International Standard Recording Code).
    /// </summary>
    IsrcLookup,

    /// <summary>
    /// Look up a release by its UPC (Universal Product Code / barcode).
    /// </summary>
    UpcLookup,

    /// <summary>
    /// Look up (search for) an artist by name.
    /// </summary>
    ArtistLookup,

    /// <summary>
    /// Look up a track or album by its provider URI, which the worker parses to identify the item.
    /// </summary>
    UriLookup,

    /// <summary>
    /// Look up an album.
    /// </summary>
    AlbumLookup,

    /// <summary>
    /// Look up the albums of a known artist (then match by album title).
    /// </summary>
    ArtistAlbumLookup,

    /// <summary>
    /// Look up a track within the context of a known album or artist.
    /// </summary>
    AlbumTrackLookup,

    /// <summary>
    /// Look up a song (track).
    /// </summary>
    SongLookup,

    /// <summary>
    /// Look up an album directly by its provider-native album id.
    /// </summary>
    AlbumIdLookup,

    /// <summary>
    /// Look up a song (track) directly by its provider-native track id.
    /// </summary>
    SongIdLookup
}
