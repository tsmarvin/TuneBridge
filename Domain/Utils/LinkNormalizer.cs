namespace TuneBridge.Domain.Utils {

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

            // Remove query string and fragment
            int queryIndex = normalized.IndexOf( '?' );
            if (queryIndex >= 0) {
                normalized = normalized[..queryIndex];
            }
            
            int fragmentIndex = normalized.IndexOf( '#' );
            if (fragmentIndex >= 0) {
                normalized = normalized[..fragmentIndex];
            }

            // Remove trailing slash
            normalized = normalized.TrimEnd( '/' );

            return normalized.ToLowerInvariant( );
        }
    }
}
