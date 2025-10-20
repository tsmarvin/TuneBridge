namespace TuneBridge.Domain.Utils {

    /// <summary>
    /// Provides consistent link normalization across the application.
    /// </summary>
    public static class LinkNormalizer {

        /// <summary>
        /// Normalizes a link by removing protocol and trailing slashes for consistent comparison.
        /// </summary>
        /// <param name="link">The link to normalize.</param>
        /// <returns>The normalized link in lowercase without protocol or trailing slashes.</returns>
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

            // Remove trailing slash
            normalized = normalized.TrimEnd( '/' );

            return normalized.ToLowerInvariant( );
        }
    }
}
