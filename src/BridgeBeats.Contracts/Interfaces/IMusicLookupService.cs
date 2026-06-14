using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// A single provider's lookup contract: resolves one provider's view of a track or album
/// from the various lookup strategies, and identifies which provider the implementation
/// serves.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by the per-provider services
/// <c>SpotifyLookupService</c>, <c>AppleMusicLookupService</c>, and <c>TidalLookupService</c>,
/// sharing <c>MusicLookupServiceBase</c> and <c>HttpMusicLookupService</c>
/// (<c>Domain/Providers/Common/</c>). Every lookup method returns <see langword="null"/> when
/// this provider has no match.
/// </remarks>
public partial interface IMusicLookupService {

    /// <summary>
    /// A concrete <see langword="static"/> auto-property declaring this interface's provider
    /// identity. It is not <see langword="static"/> <see langword="abstract"/>: no implementer
    /// satisfies or overrides it, and reading it always returns the default (invalid) enum value
    /// <c>0</c>. Effectively unused today; kept as a placeholder for future per-provider identity
    /// support.
    /// </summary>
    static SupportedProviders Provider { get; }

    /// <summary>
    /// Resolves this provider's item by free-text title and artist.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="artist">The artist to search for.</param>
    /// <returns>
    /// A task whose result is this provider's <see cref="MusicLookupResult"/>, or
    /// <see langword="null"/> when this provider has no match.
    /// </returns>
    Task<MusicLookupResult?> GetInfoAsync( string title, string artist );

    /// <summary>
    /// Resolves this provider's item by ISRC (International Standard Recording Code).
    /// </summary>
    /// <param name="isrc">The ISRC to resolve.</param>
    /// <returns>
    /// A task whose result is this provider's <see cref="MusicLookupResult"/>, or
    /// <see langword="null"/> when this provider has no match.
    /// </returns>
    Task<MusicLookupResult?> GetInfoByISRCAsync( string isrc );

    /// <summary>
    /// Resolves this provider's item by UPC (Universal Product Code).
    /// </summary>
    /// <param name="upc">The UPC to resolve.</param>
    /// <returns>
    /// A task whose result is this provider's <see cref="MusicLookupResult"/>, or
    /// <see langword="null"/> when this provider has no match.
    /// </returns>
    Task<MusicLookupResult?> GetInfoByUPCAsync( string upc );

    /// <summary>
    /// Resolves this provider's item from a provider URL, which the implementation parses.
    /// </summary>
    /// <param name="uri">The provider URL to resolve.</param>
    /// <returns>
    /// A task whose result is this provider's <see cref="MusicLookupResult"/>, or
    /// <see langword="null"/> when this provider has no match or the URL is invalid.
    /// </returns>
    Task<MusicLookupResult?> GetInfoAsync( string uri );

    /// <summary>
    /// Finds this provider's equivalent of an already-resolved result from another provider
    /// (cross-provider matching), enriching a partial lookup with this provider's data.
    /// </summary>
    /// <param name="lookup">An already-resolved <see cref="MusicLookupResult"/> from another provider to match against.</param>
    /// <returns>
    /// A task whose result is this provider's matching <see cref="MusicLookupResult"/>, or
    /// <see langword="null"/> when this provider has no match.
    /// </returns>
    Task<MusicLookupResult?> GetInfoAsync( MusicLookupResult lookup );

    /// <summary>
    /// Resolves this provider's item by its own native id.
    /// </summary>
    /// <param name="providerId">This provider's native id.</param>
    /// <param name="isAlbum"><see langword="true"/> when the id identifies an album; <see langword="false"/> for a track.</param>
    /// <returns>
    /// A task whose result is this provider's <see cref="MusicLookupResult"/>, or
    /// <see langword="null"/> when this provider has no match.
    /// </returns>
    Task<MusicLookupResult?> GetInfoByIDAsync( string providerId, bool isAlbum );
}
