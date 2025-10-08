using System.Text.Json;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    public sealed partial class DefaultMediaLinkService(
        Dictionary<SupportedProviders, IMusicLookupService> enabledProvidersCollection,
        ILogger<DefaultMediaLinkService> logger,
        JsonSerializerOptions serializerOptions
    ) : MediaLinkServiceBase( enabledProvidersCollection, logger, serializerOptions ), IMediaLinkService {

        /// <summary>
        /// Receives string input content, extracts any valid Apple Music or Spotify links from the contents,
        /// looks initial data up from both services (as applicable), combines the results to deduplicate, and
        /// then returns the MediaLinkResultsDto with data from both services (if available).
        /// </summary>
        /// <param name="content">The text containing music service URLs to parse.</param>
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
