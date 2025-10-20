using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Extensions {

    /// <summary>
    /// Extension methods for generating OpenGraph metadata from media link results.
    /// </summary>
    public static class OpenGraphExtensions {

        /// <summary>
        /// Converts a media link result to OpenGraph metadata properties.
        /// </summary>
        /// <param name="result">The media link result to convert.</param>
        /// <returns>A dictionary of OpenGraph meta tag properties.</returns>
        public static Dictionary<string, string> ToOpenGraphMetadata( this MediaLinkResult result ) {
            var metadata = new Dictionary<string, string>();

            string title = string.Empty;
            string description = string.Empty;
            string image = string.Empty;
            bool isAlbum = false;
            string artist = string.Empty;

            // Extract information from results, prioritizing the primary result
            foreach ((SupportedProviders provider, MusicLookupResultDto dto) in result.Results.OrderBy( kv => kv.Key )) {
                
                if (string.IsNullOrWhiteSpace( image ) && string.IsNullOrWhiteSpace( dto.ArtUrl ) == false) {
                    image = dto.ArtUrl;
                }

                if (string.IsNullOrWhiteSpace( title )) {
                    title = dto.Title;
                    isAlbum = dto.IsAlbum ?? false;
                    artist = dto.Artist;
                }

                if (dto.IsPrimary) {
                    title = dto.Title;
                    isAlbum = dto.IsAlbum ?? false;
                    artist = dto.Artist;
                    if (string.IsNullOrWhiteSpace( dto.ArtUrl ) == false) {
                        image = dto.ArtUrl;
                    }
                }
            }

            // Build description with provider links
            var providerLinks = result.Results
                .OrderBy( kv => kv.Key )
                .Select( kv => $"{AppExtensions.GetDescription( kv.Key )}: {kv.Value.URL}" );
            description = $"Artist: {artist}\n\nAvailable on:\n{string.Join( "\n", providerLinks )}";

            // Set OpenGraph properties
            metadata["og:type"] = "music." + (isAlbum ? "album" : "song");
            metadata["og:title"] = title;
            metadata["og:description"] = description;
            
            if (!string.IsNullOrWhiteSpace( image )) {
                metadata["og:image"] = image;
                metadata["og:image:alt"] = $"{title} artwork";
            }

            // Add music-specific metadata
            metadata["music:musician"] = artist;
            
            return metadata;
        }
    }
}
