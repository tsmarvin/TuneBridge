using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Services.LinkResolver;

namespace BridgeBeats.Core.Domain.Services.LinkResolver {

    /// <summary>
    /// In-process, synchronous implementation of <see cref="IMediaLinkService"/>. Resolves a link
    /// (or title/artist, ISRC, UPC, provider id) by calling every enabled provider directly through
    /// <see cref="MediaLinkServiceBase"/>, deduplicating and cross-platform matching via ISRC/UPC
    /// codes, combining the hits into a single <see cref="MediaLinkResult"/>, and back-filling any
    /// provider not yet present via the base class's secondary lookup. No cache, no saga, and no
    /// queue are involved; the call completes once all providers have been tried.
    /// </summary>
    /// <remarks>
    /// This is the direct counterpart to <see cref="CachingMediaLinkService"/>, which instead delegates
    /// to the distributed orchestrator. Exactly one of the two is registered, selected by the
    /// <c>useCaching</c> flag at composition time.
    /// </remarks>
    /// <param name="enabledProvidersCollection">The enabled providers and their direct lookup services, keyed by <see cref="SupportedProviders"/>.</param>
    /// <param name="logger">The logger passed to the base class for lookup diagnostics, particularly cross-platform matching failures.</param>
    /// <param name="serializerOptions">JSON options used by the base class when tracing results.</param>
    public sealed partial class DefaultMediaLinkService(
        Dictionary<SupportedProviders, IMusicLookupService> enabledProvidersCollection,
        ILogger<DefaultMediaLinkService> logger,
        JsonSerializerOptions serializerOptions
    ) : MediaLinkServiceBase( enabledProvidersCollection, logger, serializerOptions ), IMediaLinkService {

        /// <summary>
        /// Extracts every supported link from free-text <paramref name="content"/>, resolves each
        /// across all enabled providers, and streams one combined result per distinct entity as it is
        /// produced. Deduplication happens on external IDs (ISRC for tracks, UPC for albums), so
        /// different regional URLs to the same content are merged.
        /// </summary>
        /// <param name="content">Free text that may contain one or more provider links.</param>
        /// <returns>An async stream of combined multi-provider results, one per distinct entity found.</returns>
        public override async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
            await foreach (MediaLinkResult result in CombineLookupInfoAsync( await GetMusicLookupResults( content ) )) {
                yield return result;
            }
        }

        /// <summary>Resolves a track by title and artist across all enabled providers and combines the hits.</summary>
        /// <param name="title">The track or album title to search for.</param>
        /// <param name="artist">The artist name to search for.</param>
        /// <returns>The combined multi-provider result, or <see langword="null"/> when no provider matched.</returns>
        public override async Task<MediaLinkResult?> GetInfoAsync( string title, string artist )
            => await CombineLookupInfoAsync( await GetMusicLookupResults( title, artist ) );

        /// <summary>Resolves a track by ISRC across all enabled providers and combines the hits.</summary>
        /// <param name="isrc">The International Standard Recording Code identifying the track.</param>
        /// <returns>The combined multi-provider result, or <see langword="null"/> when no provider matched.</returns>
        public override async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc )
            => await CombineLookupInfoAsync( await GetMusicLookupResults( isrc, false ) );

        /// <summary>Resolves an album by UPC across all enabled providers and combines the hits.</summary>
        /// <param name="upc">The Universal Product Code identifying the album.</param>
        /// <returns>The combined multi-provider result, or <see langword="null"/> when no provider matched.</returns>
        public override async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc )
            => await CombineLookupInfoAsync( await GetMusicLookupResults( upc, true ) );

        /// <summary>
        /// Resolves an entity by a single provider's own id (the originating provider), then back-fills
        /// the remaining enabled providers to build the combined result.
        /// </summary>
        /// <param name="providerId">The entity id within the originating provider.</param>
        /// <param name="provider">The provider that <paramref name="providerId"/> belongs to.</param>
        /// <param name="isAlbum"><see langword="true"/> when the id refers to an album; otherwise a track.</param>
        /// <returns>The combined multi-provider result, or <see langword="null"/> when the originating provider returned nothing.</returns>
        public override async Task<MediaLinkResult?> GetInfoByProviderIdAsync(
            string providerId,
            SupportedProviders provider,
            bool isAlbum
        ) {
            MusicLookupResult? providerResult = await GetMusicLookupResultsByProviderId( providerId, provider, isAlbum );
            return providerResult is null
                ? null
                : await CombineLookupInfoAsync( (providerResult, provider) );
        }
    }
}
