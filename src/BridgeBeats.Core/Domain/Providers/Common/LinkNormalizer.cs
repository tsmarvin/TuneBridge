namespace BridgeBeats.Core.Domain.Providers.Common {

    /// <summary>
    /// Canonicalizes a provider link into a stable form suitable for use as a cache key.
    /// </summary>
    /// <remarks>
    /// Normalization strips the scheme and a leading <c>www.</c>, lowercases the host, drops the
    /// fragment, removes all query parameters except <c>i</c> (Apple Music's track-within-album
    /// selector), and trims a trailing slash. Two links that point at the same entity but differ
    /// only in scheme, casing, tracking parameters, or trailing slash normalize to the same string.
    /// </remarks>
    public static class LinkNormalizer {

        /// <summary>
        /// Normalizes a link to its canonical, cache-stable form.
        /// </summary>
        /// <param name="link">The raw link to normalize.</param>
        /// <returns>
        /// The normalized link, or <see cref="string.Empty"/> when <paramref name="link"/> is
        /// <see langword="null"/>, empty, or whitespace. Only the host portion is lowercased; the
        /// path and ID portions preserve their original case, since provider IDs are case-sensitive.
        /// </returns>
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
        /// Drops tracking and incidental query parameters, preserving only the <c>i</c> parameter
        /// (Apple Music's track-within-album selector).
        /// </summary>
        /// <param name="queryString">The query portion of a path, including the leading <c>?</c>.</param>
        /// <returns>
        /// A query string containing only the preserved parameters (prefixed with <c>?</c>), or
        /// <see cref="string.Empty"/> when nothing is preserved or the input is not a query string.
        /// </returns>
        private static string FilterTrackingParameters( string queryString ) {
            if (string.IsNullOrEmpty( queryString ) || !queryString.StartsWith( '?' )) {
                return string.Empty;
            }

            // Parse query parameters
            string[] parameters = queryString[1..].Split( '&' );
            List<string> preservedParams = new( );

            foreach (string param in parameters) {
                if (string.IsNullOrWhiteSpace( param )) {
                    continue;
                }

                // Get parameter name (before '=' if present)
                string paramName = param.Split( '=' )[0];

                // Only preserve Apple Music's ?i= parameter (track ID in album)
                if (paramName.Equals( "i", StringComparison.OrdinalIgnoreCase )) {
                    preservedParams.Add( param );
                }
            }

            // Return filtered query string
            return preservedParams.Count > 0 ? "?" + string.Join( "&", preservedParams ) : string.Empty;
        }
    }
}
