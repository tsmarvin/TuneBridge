namespace BridgeBeats.Core.Domain.Providers.Common {

    /// <summary>
    /// Provides consistent link normalization across the application.
    /// </summary>
    public static class LinkNormalizer {

        /// <summary>
        /// Normalizes a link by removing protocol, www prefix, trailing slashes, and tracking query parameters
        /// for consistent comparison while preserving case-sensitive IDs and semantic query parameters.
        /// </summary>
        /// <param name="link">The link to normalize.</param>
        /// <returns>The normalized link without protocol, www prefix, tracking params, or trailing slashes.
        /// Domain is lowercased, but path/ID portions preserve original case.</returns>
        /// <remarks>
        /// Removes tracking parameters (e.g., Spotify's ?si=) but preserves semantic parameters
        /// (e.g., Apple Music's ?i= which identifies specific tracks within albums).
        /// IDs are case-sensitive and must be preserved for correct matching.
        /// </remarks>
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

            // Split domain from path to handle case normalization correctly
            // Domain should be case-insensitive, but path/IDs are case-sensitive
            int pathStart = normalized.IndexOf( '/' );
            string domain;
            string path;

            if (pathStart >= 0) {
                domain = normalized[..pathStart].ToLowerInvariant( );
                path = normalized[pathStart..];
            } else {
                domain = normalized.ToLowerInvariant( );
                path = string.Empty;
            }

            // Remove fragments from path
            int fragmentIndex = path.IndexOf( '#' );
            if (fragmentIndex >= 0) {
                path = path[..fragmentIndex];
            }

            // Remove tracking query parameters, but preserve semantic parameters
            // Spotify uses ?si= for tracking (should be removed)
            // Apple Music uses ?i= for track ID in album context (should be preserved)
            int queryIndex = path.IndexOf( '?' );
            if (queryIndex >= 0) {
                string basePath = path[..queryIndex];
                string queryString = path[queryIndex..];

                // Parse query string and filter out tracking parameters
                string filteredQuery = FilterTrackingParameters( queryString );

                path = basePath + filteredQuery;
            }

            // Combine domain and path, remove trailing slash
            normalized = (domain + path).TrimEnd( '/' );

            return normalized;
        }

        /// <summary>
        /// Filters out tracking query parameters while preserving semantic parameters.
        /// </summary>
        /// <param name="queryString">Query string including the leading '?'</param>
        /// <returns>Filtered query string with only semantic parameters, or empty if all params were tracking</returns>
        private static string FilterTrackingParameters( string queryString ) {
            if (string.IsNullOrEmpty( queryString ) || !queryString.StartsWith( '?' )) {
                return string.Empty;
            }

            // Known tracking parameters to remove (case-insensitive parameter names)
            HashSet<string> trackingParams = new( StringComparer.OrdinalIgnoreCase ) {
                "si",     // Spotify tracking
                "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term", // UTM tracking
                "fbclid", // Facebook click ID
                "gclid",  // Google click ID
                "at",     // Apple affiliate token
                "ls",     // Apple Music tracking
                "uo",     // Apple Music tracking
                "app"     // Generic app tracking
            };

            // Parse query parameters
            string[] parameters = queryString[1..].Split( '&' );
            List<string> preservedParams = new( );

            foreach (string param in parameters) {
                if (string.IsNullOrWhiteSpace( param )) {
                    continue;
                }

                // Get parameter name (before '=' if present)
                string paramName = param.Split( '=' )[0];

                // Keep parameter if it's not a tracking parameter
                if (!trackingParams.Contains( paramName )) {
                    preservedParams.Add( param );
                }
            }

            // Return filtered query string
            return preservedParams.Count > 0 ? "?" + string.Join( "&", preservedParams ) : string.Empty;
        }
    }
}
