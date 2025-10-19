using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Extensions {

    /// <summary>
    /// Extension methods for converting MediaLinkResult to OpenGraph metadata.
    /// </summary>
    public static class OpenGraphExtensions {

        /// <summary>
        /// Generates OpenGraph metadata tags for a MediaLinkResult.
        /// </summary>
        /// <param name="result">The MediaLinkResult to generate metadata for.</param>
        /// <param name="cardUrl">The URL where the card is hosted.</param>
        /// <returns>A dictionary of OpenGraph meta tag properties.</returns>
        public static Dictionary<string, string> ToOpenGraphTags( this MediaLinkResult result, string cardUrl ) {
            Dictionary<string, string> tags = new( );

            if (result.Results.Count == 0) {
                return tags;
            }

            // Get primary result or first available
            KeyValuePair<SupportedProviders, MusicLookupResultDto> primary = result.Results
                .FirstOrDefault( kv => kv.Value.IsPrimary );
            
            if (primary.Value == null) {
                primary = result.Results.First( );
            }

            MusicLookupResultDto dto = primary.Value;

            // Basic OpenGraph tags
            tags["og:type"] = "music.song";
            tags["og:url"] = cardUrl;
            tags["og:title"] = GetTitle( dto.IsAlbum, dto.Title );
            tags["og:description"] = GetDescription( result, dto );
            
            if (!string.IsNullOrWhiteSpace( dto.ArtUrl )) {
                tags["og:image"] = dto.ArtUrl;
                tags["og:image:alt"] = $"{dto.Title} by {dto.Artist}";
            }

            // Music-specific tags
            tags["music:musician"] = dto.Artist;
            
            if (dto.IsAlbum == false) {
                tags["og:type"] = "music.song";
            } else if (dto.IsAlbum == true) {
                tags["og:type"] = "music.album";
            }

            // Twitter Card tags for better Twitter embedding
            tags["twitter:card"] = "summary_large_image";
            tags["twitter:title"] = GetTitle( dto.IsAlbum, dto.Title );
            tags["twitter:description"] = GetDescription( result, dto );
            if (!string.IsNullOrWhiteSpace( dto.ArtUrl )) {
                tags["twitter:image"] = dto.ArtUrl;
            }

            return tags;
        }

        private static string GetTitle( bool? isAlbum, string title ) {
            string prefix = isAlbum switch {
                true => "Album: ",
                false => "Song: ",
                _ => "Title: "
            };
            return prefix + title;
        }

        private static string GetDescription( MediaLinkResult result, MusicLookupResultDto primaryDto ) {
            string desc = $"Artist: {primaryDto.Artist}";
            
            if (!string.IsNullOrWhiteSpace( primaryDto.ExternalId )) {
                string idType = primaryDto.IsAlbum == true ? "UPC" : "ISRC";
                desc += $"\n{idType}: {primaryDto.ExternalId}";
            }

            List<string> providerLinks = [];
            foreach ((SupportedProviders provider, MusicLookupResultDto dto) in result.Results.OrderBy( kv => kv.Key )) {
                string providerName = provider.ToString( );
                if (provider == SupportedProviders.AppleMusic) {
                    providerName = "Apple Music";
                }
                providerLinks.Add( $"{providerName}: {dto.URL}" );
            }

            if (providerLinks.Count > 0) {
                desc += "\n\nAvailable on:\n" + string.Join( "\n", providerLinks );
            }

            return desc;
        }
    }

}
