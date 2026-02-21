using System.Text.RegularExpressions;

namespace BridgeBeats.Core.Domain.Providers.Common {

    /// <summary>
    /// Provides consistent link normalization across the application.
    /// </summary>
    public static partial class LinkNormalizer {

        /// <summary>
        /// Normalizes a link by removing protocol, query strings, fragments, www prefix, and trailing slashes for consistent comparison.
        /// </summary>
        /// <param name="link">The link to normalize.</param>
        /// <returns>The normalized link in lowercase without protocol, query strings, fragments, www prefix, or trailing slashes.</returns>
        public static string Normalize( string link ) {
            if (string.IsNullOrWhiteSpace( link )) {
                return string.Empty;
            }

            // Remove protocol (http:// or https://)
            string normalized = link.Trim( );
            if (normalized.StartsWith( "https://", StringComparison.OrdinalIgnoreCase )) {
                normalized = normalized[8..];
            } else if (normalized.StartsWith( "http://", StringComparison.OrdinalIgnoreCase )) {
                normalized = normalized[7..];
            }

            // Remove www. prefix
            if (normalized.StartsWith( "www.", StringComparison.OrdinalIgnoreCase )) {
                normalized = normalized[4..];
            }

            // Remove trailing slash
            return normalized.TrimEnd( '/' );
        }

        /// <summary>
        /// Normalizes a Spotify URL by removing query parameters and fragments while preserving the full URL structure.
        /// Reconstructs a clean canonical URL from the ID and entity type.
        /// </summary>
        /// <param name="url">The Spotify URL to normalize (may contain query parameters like ?si=).</param>
        /// <returns>
        /// The normalized URL without query parameters, or the original URL if it cannot be parsed.
        /// Returns empty string if the input is null or empty.
        /// </returns>
        /// <remarks>
        /// This ensures that URLs like https://open.spotify.com/track/ID?si=xyz are normalized to
        /// https://open.spotify.com/track/ID for consistent caching and comparison.
        /// </remarks>
        public static string NormalizeSpotifyUrl( string? url ) {
            if (string.IsNullOrWhiteSpace( url )) {
                return string.Empty;
            }

            // Use the regex to extract the type and ID
            Match match = s_spotifyLink.Match( url );
            if (match.Success) {
                string type = match.Groups["type"].Value.ToLowerInvariant( );
                string id = match.Groups["id"].Value;
                return $"https://open.spotify.com/{type}/{id}";
            }

            // If we can't parse it, return the original URL
            return url;
        }

        private static readonly Regex s_spotifyLink = SpotifyMusicLink();
        [GeneratedRegex( @"(?:open\.spotify\.com/)(?<type>track|album|prerelease)/(?<id>[A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled )]
        private static partial Regex SpotifyMusicLink( );
    }
}
