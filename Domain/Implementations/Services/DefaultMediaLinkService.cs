using System.Text.Json;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Default implementation of <see cref="IMediaLinkService"/> that coordinates parallel lookups across
    /// all configured music providers (Apple Music, Spotify) and aggregates results. This service handles
    /// deduplication, cross-platform matching via ISRC/UPC codes, and intelligent result merging to provide
    /// a unified view of music content across streaming services.
    /// </summary>
    /// <param name="enabledProvidersCollection">
    /// Dictionary of active provider services keyed by <see cref="SupportedProviders"/>. Typically includes
    /// AppleMusicLookupService and SpotifyLookupService, injected via DI.
    /// </param>
    /// <param name="logger">Logger for diagnostic information, particularly useful for debugging cross-platform matching failures.</param>
    /// <param name="serializerOptions">JSON serialization options for logging complex objects during troubleshooting.</param>
    public sealed partial class DefaultMediaLinkService(
        Dictionary<SupportedProviders, IMusicLookupService> enabledProvidersCollection,
        ILogger<DefaultMediaLinkService> logger,
        JsonSerializerOptions serializerOptions
    ) : MediaLinkServiceBase( enabledProvidersCollection, logger, serializerOptions ), IMediaLinkService {

        /// <summary>
        /// Parses text for music service URLs (Spotify/Apple Music), queries each provider's API to extract
        /// metadata, then cross-references results across platforms using ISRC/UPC matching. Returns one
        /// <see cref="MediaLinkResult"/> per unique track/album with URLs from all providers where available.
        /// </summary>
        /// <param name="content">
        /// Free-form text potentially containing music URLs. The parser uses regex to extract supported URL patterns.
        /// Multiple URLs to the same content are automatically deduplicated.
        /// </param>
        /// <returns>
        /// Async enumerable yielding results progressively as they're discovered. Each result represents a unique
        /// track/album with combined metadata from all providers. Empty if no valid music URLs were found.
        /// </returns>
        /// <remarks>
        /// The method streams results to enable progressive processing in UIs. Deduplication happens based on
        /// external IDs (ISRC for tracks, UPC for albums), so different regional URLs to the same content are merged.
        /// </remarks>
        public override async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
            await foreach (MediaLinkResult result in CombineLookupInfoAsync( await GetMusicLookupResults( content ) )) {
                yield return result;
            }
        }

        public override async Task<MediaLinkResult?> GetInfoAsync( string title, string artist )
            => await CombineLookupInfoAsync( await GetMusicLookupResults( title, artist ) );

        public override async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc )
            => await CombineLookupInfoAsync( await GetMusicLookupResults( isrc, false ) );

        public override async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc )
            => await CombineLookupInfoAsync( await GetMusicLookupResults( upc, true ) );

    }
}
