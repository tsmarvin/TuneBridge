using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Decorator for <see cref="IMediaLinkService"/> that adds caching with Bluesky PDS storage.
    /// This service checks the cache before performing lookups and stores results for future use.
    /// </summary>
    public class CachedMediaLinkService : IMediaLinkService {

        private readonly IMediaLinkService _innerService;
        private readonly IMediaLinkCacheService _cacheService;
        private readonly ILogger<CachedMediaLinkService> _logger;

        public CachedMediaLinkService(
            IMediaLinkService innerService,
            IMediaLinkCacheService cacheService,
            ILogger<CachedMediaLinkService> logger
        ) {
            _innerService = innerService;
            _cacheService = cacheService;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetInfoAsync( string title, string artist ) {
            // Direct title/artist lookups are not cached (no input link to track)
            return await _innerService.GetInfoAsync( title, artist );
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc ) {
            // ISRC lookups are not cached (no input link to track)
            return await _innerService.GetInfoByISRCAsync( isrc );
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc ) {
            // UPC lookups are not cached (no input link to track)
            return await _innerService.GetInfoByUPCAsync( upc );
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
            // Extract links from content for cache checking
            var extractedLinks = ExtractLinks( content );

            // Track which links we've already processed to avoid duplicates
            var processedLinks = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            // Track stale entries that need refreshing: recordUri -> list of input links
            var staleEntries = new Dictionary<string, HashSet<string>>( );

            // Check cache for each extracted link
            foreach (string link in extractedLinks) {
                if (processedLinks.Contains( link )) {
                    continue;
                }

                var cachedResult = await _cacheService.TryGetCachedResultAsync( link );
                if (cachedResult.HasValue) {
                    processedLinks.Add( link );
                    
                    if (cachedResult.Value.isStale) {
                        // Track stale entry for later refresh
                        if (!staleEntries.ContainsKey( cachedResult.Value.recordUri )) {
                            staleEntries[cachedResult.Value.recordUri] = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
                        }
                        staleEntries[cachedResult.Value.recordUri].Add( link );
                    } else {
                        // Return fresh cached result
                        _logger.LogInformation( "Using fresh cached result for link: {link}", link );
                        yield return cachedResult.Value.result;
                    }
                }
            }

            // Build content with only non-cached links for lookup
            var nonCachedLinks = extractedLinks.Except( processedLinks ).ToList( );
            
            // Perform lookup for non-cached and stale links
            if (nonCachedLinks.Count > 0 || staleEntries.Count > 0) {
                string contentWithNonCachedLinks = string.Join( " ", nonCachedLinks.Select( l => $"https://{l}" ) );
                
                // Add stale entry links to trigger a fresh lookup
                foreach (var staleLinks in staleEntries.Values) {
                    contentWithNonCachedLinks += " " + string.Join( " ", staleLinks.Select( l => $"https://{l}" ) );
                }

                contentWithNonCachedLinks = contentWithNonCachedLinks.Trim( );
                
                if (string.IsNullOrEmpty( contentWithNonCachedLinks )) {
                    yield break;
                }

                await foreach (MediaLinkResult result in _innerService.GetInfoAsync( contentWithNonCachedLinks )) {
                    // Determine which input links generated this result
                    var resultInputLinks = result._inputLinks
                        .Select( l => l.Replace( "https://", "" ).Replace( "http://", "" ).TrimEnd( '/' ).ToLowerInvariant( ) )
                        .ToList( );

                    try {
                        // Check if this result matches a stale entry
                        string? matchingStaleRecordUri = null;
                        foreach (var (recordUri, staleLinks) in staleEntries) {
                            if (resultInputLinks.Any( ril => staleLinks.Contains( ril ) )) {
                                matchingStaleRecordUri = recordUri;
                                break;
                            }
                        }

                        if (matchingStaleRecordUri != null) {
                            // Update the existing stale entry
                            await _cacheService.UpdateCacheEntryAsync( matchingStaleRecordUri, result, resultInputLinks );
                            _logger.LogInformation( "Refreshed stale cache entry: {uri}", matchingStaleRecordUri );
                            
                            // Remove from stale entries to avoid reprocessing
                            _ = staleEntries.Remove( matchingStaleRecordUri );
                        } else {
                            // Cache as a new result
                            string recordUri = await _cacheService.CacheResultAsync( result, resultInputLinks );
                            _logger.LogInformation( "Cached new result to Bluesky: {uri}", recordUri );
                        }
                    } catch (Exception ex) {
                        _logger.LogError( ex, "Failed to cache result, continuing without caching" );
                    }

                    yield return result;
                }
            }
        }

        /// <summary>
        /// Extracts normalized links from content.
        /// </summary>
        private static List<string> ExtractLinks( string content ) {
            var links = new List<string>( );
            
            // Simple extraction - look for https:// or http:// followed by URL
            var matches = System.Text.RegularExpressions.Regex.Matches( 
                content, 
                @"https?://([^\s]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase 
            );

            foreach (System.Text.RegularExpressions.Match match in matches) {
                if (match.Groups.Count > 1) {
                    string link = match.Groups[1].Value.TrimEnd( '/' ).ToLowerInvariant( );
                    links.Add( link );
                }
            }

            return links;
        }
    }
}
