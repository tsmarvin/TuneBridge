using System.Text.RegularExpressions;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Decorator for <see cref="IMediaLinkService"/> that adds caching with Bluesky PDS storage.
    /// This service checks the cache before performing lookups and stores results for future use.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="CachedMediaLinkService"/> class.
    /// </remarks>
    /// <param name="innerService">The underlying media link service to decorate with caching.</param>
    /// <param name="cacheService">The cache service for storing and retrieving results.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public partial class CachedMediaLinkService(
        IMediaLinkService innerService,
        IMediaLinkCacheService cacheService,
        ILogger<CachedMediaLinkService> logger
    ) : IMediaLinkService {

        // Static compiled regex for URL extraction (performance optimization)
        private static readonly Regex s_urlRegex = UrlRegex( );

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetInfoAsync( string title, string artist ) {
            // Direct title/artist lookups are not cached (no input link to track)
            return await innerService.GetInfoAsync( title, artist );
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc ) {
            // ISRC lookups are not cached (no input link to track)
            return await innerService.GetInfoByISRCAsync( isrc );
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc ) {
            // UPC lookups are not cached (no input link to track)
            return await innerService.GetInfoByUPCAsync( upc );
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
            // Extract links from content for cache checking
            List<string> extractedLinks = ExtractLinks( content );

            // Track which links we've already processed to avoid duplicates
            HashSet<string> processedLinks = new( StringComparer.OrdinalIgnoreCase );
            // Track stale entries that need refreshing: recordUri -> list of input links
            Dictionary<string, HashSet<string>> staleEntries = [];
            // Track fresh entries to associate new links later: recordUri -> list of cached input links
            Dictionary<string, HashSet<string>> freshEntries = [];

            // Check cache for each extracted link
            foreach (string link in extractedLinks) {
                if (processedLinks.Contains( link )) {
                    continue;
                }

                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await cacheService.TryGetCachedResultAsync( link );
                if (cachedResult.HasValue) {
                    _ = processedLinks.Add( link );

                    if (cachedResult.Value.isStale) {
                        // Track stale entry for later refresh
                        if (!staleEntries.TryGetValue( cachedResult.Value.recordUri, out HashSet<string>? value )) {
                            value = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
                            staleEntries[cachedResult.Value.recordUri] = value;
                        }
                        _ = value.Add( link );
                    } else {
                        // Return fresh cached result
                        logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri}", cachedResult.Value.recordUri );

                        // Track fresh entry to associate new links later
                        if (!freshEntries.TryGetValue( cachedResult.Value.recordUri, out HashSet<string>? value )) {
                            value = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
                            freshEntries[cachedResult.Value.recordUri] = value;
                        }
                        _ = value.Add( link );

                        yield return cachedResult.Value.result;
                    }
                }
            }

            // Build content with only non-cached links for lookup
            List<string> nonCachedLinks = [.. extractedLinks.Except( processedLinks )];

            // Perform lookup for non-cached and stale links
            if (nonCachedLinks.Count > 0 || staleEntries.Count > 0) {
                string contentWithNonCachedLinks = string.Join( " ", nonCachedLinks.Select( l => $"https://{l}" ) );

                // Add stale entry links to trigger a fresh lookup
                foreach (HashSet<string> staleLinks in staleEntries.Values) {
                    contentWithNonCachedLinks += " " + string.Join( " ", staleLinks.Select( l => $"https://{l}" ) );
                }

                contentWithNonCachedLinks = contentWithNonCachedLinks.Trim( );

                if (string.IsNullOrEmpty( contentWithNonCachedLinks )) {
                    yield break;
                }

                await foreach (MediaLinkResult result in innerService.GetInfoAsync( contentWithNonCachedLinks )) {
                    // Determine which input links generated this result
                    List<string> resultInputLinks = [.. result._inputLinks.Select( LinkNormalizer.Normalize )];

                    try {
                        // Check if this result matches a stale entry
                        string? matchingStaleRecordUri = null;
                        foreach ((string recordUri, HashSet<string> staleLinks) in staleEntries) {
                            if (resultInputLinks.Any( ril => staleLinks.Contains( ril ) )) {
                                matchingStaleRecordUri = recordUri;
                                break;
                            }
                        }

                        // Check if this result matches a fresh entry (new links for existing result)
                        string? matchingFreshRecordUri = null;
                        if (matchingStaleRecordUri == null) {
                            foreach ((string recordUri, HashSet<string> freshLinks) in freshEntries) {
                                if (resultInputLinks.Any( ril => freshLinks.Contains( ril ) )) {
                                    matchingFreshRecordUri = recordUri;
                                    break;
                                }
                            }
                        }

                        if (matchingStaleRecordUri != null) {
                            // Update the existing stale entry
                            await cacheService.UpdateCacheEntryAsync( matchingStaleRecordUri, result, resultInputLinks );
                            logger.LogInformation( "Refreshed stale cache entry: {uri}", matchingStaleRecordUri );

                            // Remove from stale entries to avoid reprocessing
                            _ = staleEntries.Remove( matchingStaleRecordUri );
                        } else if (matchingFreshRecordUri != null) {
                            // Add new links to existing fresh cache entry
                            await cacheService.AddInputLinksAsync( matchingFreshRecordUri, resultInputLinks );
                            logger.LogInformation( "Added new links to existing fresh cache entry: {uri}", matchingFreshRecordUri );
                        } else {
                            // Cache as a new result
                            string recordUri = await cacheService.CacheResultAsync( result, resultInputLinks );
                            logger.LogInformation( "Cached new result to Bluesky: {uri}", recordUri );
                        }
                    } catch (Exception ex) {
                        logger.LogError( ex, "Failed to cache result, continuing without caching" );
                    }

                    yield return result;
                }
            }
        }

        /// <summary>
        /// Extracts normalized links from content.
        /// </summary>
        private static List<string> ExtractLinks( string content ) {
            List<string> links = [];

            // Simple extraction - look for https:// or http:// followed by URL
            MatchCollection matches = s_urlRegex.Matches( content );

            foreach (System.Text.RegularExpressions.Match match in matches) {
                if (match.Success) {
                    // Use full match to preserve original scheme
                    string link = match.Value;
                    // Trim common trailing punctuation to avoid false negatives at sentence boundaries
                    link = link.TrimEnd( '.', ',', '!', '?', ':', ';', ')', ']', '}' );
                    // Normalize the link
                    string normalizedLink = LinkNormalizer.Normalize( link );
                    links.Add( normalizedLink );
                }
            }

            return links;
        }

        [GeneratedRegex( @"https?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US" )]
        private static partial Regex UrlRegex( );
    }
}
