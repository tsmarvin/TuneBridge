namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// One provider's resolved item (album or track) within a cross-provider lookup, carrying the
/// metadata needed to identify and link to it on a streaming platform, including the standardized
/// identifier (ISRC/UPC) used for cross-platform matching. A mutable in-memory transfer object:
/// string fields default to empty rather than <see langword="null"/> so callers can populate them
/// progressively. The persisted twin is <see cref="Records.ProviderResultRecord"/>.
/// </summary>
/// <remarks>
/// Two instances are equal when their <see cref="ExternalId"/>, <see cref="Artist"/>,
/// <see cref="Title"/>, <see cref="URL"/>, <see cref="ArtUrl"/>, <see cref="MarketRegion"/>, and
/// <see cref="IsAlbum"/> values match. The internal <see cref="IsPrimary"/> flag is excluded from
/// equality, since it is a processing hint rather than identifying information.
/// </remarks>
public sealed class MusicLookupResult {

    /// <summary>
    /// The primary artist name as returned by the provider. For tracks this is typically the main
    /// artist even when several are credited; for albums it is the album artist. Defaults to an empty
    /// string until populated.
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// The title of the track or album as listed in the provider's catalog. May include descriptors
    /// such as "(Deluxe Edition)", "(Remastered)", or "- Single" depending on the provider. Defaults
    /// to an empty string until populated.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The standardized global identifier for the recording or album: the ISRC for tracks, the UPC
    /// for albums. These identifiers enable reliable cross-platform matching because they are
    /// consistent across providers. Defaults to an empty string, which is also used when the provider
    /// returns no external identifier (some older or regional catalog items lack one).
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// The provider's web link to the track or album. The URL is shareable and opens in the
    /// provider's app when installed. Defaults to an empty string until populated.
    /// </summary>
    public string URL { get; set; } = string.Empty;

    /// <summary>
    /// URL to the cover artwork (album artwork for both tracks and albums). Defaults to an empty
    /// string when no artwork is available.
    /// </summary>
    public string ArtUrl { get; set; } = string.Empty;

    /// <summary>
    /// The market / storefront region code (lowercase ISO 3166-1 alpha-2, e.g. <c>"us"</c>) the item
    /// was resolved in. Defaults to <c>"us"</c>.
    /// </summary>
    /// <remarks>
    /// For Apple Music this is the default storefront used for ISRC, UPC, and artist/title searches;
    /// for URI-based searches the storefront is taken from the URL instead. Availability and content
    /// versions can differ between markets.
    /// </remarks>
    public string MarketRegion { get; set; } = "us";

    /// <summary>
    /// <see langword="true"/> when the item is an album, <see langword="false"/> when it is a
    /// track, or <see langword="null"/> when not determined.
    /// </summary>
    /// <remarks>
    /// Selects which external-identifier type is expected (UPC for albums, ISRC for tracks) and
    /// influences how the result is displayed.
    /// </remarks>
    public bool? IsAlbum { get; set; }

    /// <summary>
    /// Marks this result as the anchor that seeded the cross-provider lookup (the item the user
    /// started from), so its metadata is preferred when providers disagree. Set within Core when
    /// combining results, not serialized, and excluded from value equality. Visible only to the
    /// projects granted internal access (Core, Web, Tests).
    /// </summary>
    internal bool IsPrimary { get; set; }

    /// <summary>
    /// Determines value equality against another <see cref="MusicLookupResult"/> by comparing the
    /// seven public fields (<see cref="ExternalId"/>, <see cref="Artist"/>, <see cref="Title"/>,
    /// <see cref="URL"/>, <see cref="ArtUrl"/>, <see cref="MarketRegion"/>, <see cref="IsAlbum"/>).
    /// The internal <see cref="IsPrimary"/> flag is not considered.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="obj"/> is a <see cref="MusicLookupResult"/> whose
    /// seven public fields all equal this one's.
    /// </returns>
    public override bool Equals( object? obj ) {
        if (
            obj is not null &&
            obj.GetType( ) == typeof( MusicLookupResult )
        ) {
            MusicLookupResult objCast = (MusicLookupResult)obj;
            return
                objCast.ExternalId == ExternalId &&
                objCast.Artist == Artist &&
                objCast.Title == Title &&
                objCast.URL == URL &&
                objCast.ArtUrl == ArtUrl &&
                objCast.MarketRegion == MarketRegion &&
                objCast.IsAlbum == IsAlbum;
        }
        return false;
    }

    /// <summary>
    /// Returns a hash code combining the same seven public fields used by
    /// <see cref="Equals(object?)"/>, so equal instances hash equally. The internal
    /// <see cref="IsPrimary"/> flag is excluded.
    /// </summary>
    /// <returns>A hash code derived from the seven public fields.</returns>
    public override int GetHashCode( ) {
        return Artist.GetHashCode( ) +
        Title.GetHashCode( ) +
        ExternalId.GetHashCode( ) +
        URL.GetHashCode( ) +
        ArtUrl.GetHashCode( ) +
        MarketRegion.GetHashCode( ) +
        IsAlbum.GetHashCode( );
    }
}
