using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Façade over cross-provider lookup that aggregates music metadata from the configured
/// streaming providers and returns the unified in-memory <see cref="MediaLinkResult"/> for a
/// request, hiding the orchestration and caching beneath.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>CachingMediaLinkService</c> (which wraps the
/// orchestrator and cache), with <c>DefaultMediaLinkService</c> and the shared
/// <c>MediaLinkServiceBase</c> fanning out across the per-provider
/// <see cref="IMusicLookupService"/> instances
/// (<c>Domain/Services/LinkResolver/</c>). Enabled providers are queried in parallel and matching
/// results are combined on external ids (ISRC/UPC) so the same track or album is identified
/// across platforms. Single-result methods return <see langword="null"/> when nothing matched.
/// </remarks>
public interface IMediaLinkService {

    /// <summary>
    /// Resolves a result by free-text title and artist, deduplicating content found on multiple
    /// platforms.
    /// </summary>
    /// <param name="title">The title to search for.</param>
    /// <param name="artist">The primary artist name; the main credited artist gives the best match.</param>
    /// <returns>
    /// A task whose result is the aggregated <see cref="MediaLinkResult"/>, or
    /// <see langword="null"/> when no match was found.
    /// </returns>
    Task<MediaLinkResult?> GetInfoAsync( string title, string artist );

    /// <summary>
    /// Resolves a result by ISRC (International Standard Recording Code), the most reliable
    /// cross-platform match because ISRCs are standardized per recording.
    /// </summary>
    /// <param name="isrc">The ISRC to resolve. Hyphens are optional and handled automatically.</param>
    /// <returns>
    /// A task whose result is the aggregated <see cref="MediaLinkResult"/>, or
    /// <see langword="null"/> when no match was found.
    /// </returns>
    Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc );

    /// <summary>
    /// Resolves a result by UPC (Universal Product Code), which identifies an album release more
    /// reliably than a title search for special, deluxe, or international editions.
    /// </summary>
    /// <param name="upc">The UPC to resolve; preserve leading zeros for accurate matching.</param>
    /// <returns>
    /// A task whose result is the aggregated <see cref="MediaLinkResult"/>, or
    /// <see langword="null"/> when no match was found.
    /// </returns>
    Task<MediaLinkResult?> GetInfoByUPCAsync( string upc );

    /// <summary>
    /// Parses input content for supported music URLs, resolves each, and streams the aggregated
    /// results as they become available. This is the primary path used by the Discord bot to turn
    /// a shared link into multi-platform links.
    /// </summary>
    /// <param name="content">The raw input content to resolve, for example one or more provider URLs.</param>
    /// <returns>
    /// An asynchronous sequence of <see cref="MediaLinkResult"/> values, one per unique track or
    /// album found; earlier items may be partial and later items more complete as additional
    /// providers respond.
    /// </returns>
    IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content );

    /// <summary>
    /// Resolves a result starting from a single provider's native id, then matches the content
    /// across the other configured providers.
    /// </summary>
    /// <param name="providerId">The provider-native id to start from.</param>
    /// <param name="provider">The provider that <paramref name="providerId"/> belongs to.</param>
    /// <param name="isAlbum"><see langword="true"/> when the id identifies an album; <see langword="false"/> for a track.</param>
    /// <returns>
    /// A task whose result is the aggregated <see cref="MediaLinkResult"/>, or
    /// <see langword="null"/> when no match was found.
    /// </returns>
    Task<MediaLinkResult?> GetInfoByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum );
}
