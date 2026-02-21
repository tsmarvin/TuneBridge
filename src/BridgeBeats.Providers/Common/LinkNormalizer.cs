namespace BridgeBeats.Providers.Common {

    /// <summary>
    /// Provides consistent link normalization across the application.
    /// </summary>
    public static class LinkNormalizer {

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
        /// Normalizes a URL by removing tracking query parameters while preserving the protocol and domain.
        /// This ensures URLs with different tracking parameters (e.g., ?si=xxx) are treated as identical for caching.
        /// </summary>
        /// <param name="url">The URL to normalize.</param>
        /// <returns>The normalized URL without tracking parameters.</returns>
        public static string NormalizeUrl( string url ) {
            if (string.IsNullOrWhiteSpace( url )) {
                return string.Empty;
            }

            string trimmedUrl = url.Trim( );

            // Remove query parameters and fragments
            int queryIndex = trimmedUrl.IndexOf( '?' );
            int fragmentIndex = trimmedUrl.IndexOf( '#' );

            int cutoffIndex = -1;
            if (queryIndex >= 0 && fragmentIndex >= 0) {
                cutoffIndex = Math.Min( queryIndex, fragmentIndex );
            } else if (queryIndex >= 0) {
                cutoffIndex = queryIndex;
            } else if (fragmentIndex >= 0) {
                cutoffIndex = fragmentIndex;
            }

            if (cutoffIndex >= 0) {
                trimmedUrl = trimmedUrl[..cutoffIndex];
            }

            return trimmedUrl;
        }
    }
}
