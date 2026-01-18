using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Services.LinkResolver {

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
            Dictionary<string, string> metadata = [];

            string title = string.Empty;
            string description = string.Empty;
            string image = string.Empty;
            bool isAlbum = false;
            string artist = string.Empty;
            string externalId = string.Empty;
            SupportedProviders? primaryProvider = null;

            // Extract information from results, prioritizing the primary result
            foreach ((SupportedProviders provider, MusicLookupResult dto) in result.Results.OrderBy( kv => kv.Key )) {

                if (string.IsNullOrWhiteSpace( image ) && !string.IsNullOrWhiteSpace( dto.ArtUrl )) {
                    image = dto.ArtUrl;
                }

                if (string.IsNullOrWhiteSpace( externalId ) && !string.IsNullOrWhiteSpace( dto.ExternalId )) {
                    externalId = dto.ExternalId;
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
                    primaryProvider = provider;
                    if (string.IsNullOrWhiteSpace( dto.ArtUrl ) == false) {
                        image = dto.ArtUrl;
                    }
                    if (string.IsNullOrWhiteSpace( dto.ExternalId ) == false) {
                        externalId = dto.ExternalId;
                    }
                }
            }

            // Build description with artist and ISRC/UPC (similar to old Discord embed format)
            description = $"Artist: {artist}";
            if (!string.IsNullOrWhiteSpace( externalId )) {
                string externalIdPrefix = isAlbum ? "UPC" : "ISRC";
                description += $"\n{externalIdPrefix}: {externalId}";
            }

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

            // Add theme color based on primary provider (for Discord embed color)
            if (primaryProvider.HasValue) {
                string themeColor = GetPrimaryProviderColorHex( primaryProvider.Value );
                metadata["theme-color"] = themeColor;
            }

            return metadata;
        }

        /// <summary>
        /// Gets the hex color code for a provider's theme color.
        /// </summary>
        /// <param name="provider">The music provider.</param>
        /// <returns>Hex color code string.</returns>
        private static string GetPrimaryProviderColorHex( SupportedProviders provider )
            => provider switch {
                SupportedProviders.AppleMusic => "#D60017",
                SupportedProviders.Spotify => "#1ED760",
                SupportedProviders.Tidal => "#FFFFFF",
                _ => "#6366F1"  // Default purple
            };
    }
}
