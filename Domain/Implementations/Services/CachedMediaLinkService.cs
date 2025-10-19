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
            var cachedResults = new List<(MediaLinkResult result, string recordUri, List<string> inputLinks)>( );

            // Check cache for each extracted link
            foreach (string link in extractedLinks) {
                if (processedLinks.Contains( link )) {
                    continue;
                }

                var cachedResult = await _cacheService.TryGetCachedResultAsync( link );
                if (cachedResult.HasValue) {
                    _logger.LogInformation( "Using cached result for link: {link}", link );
                    processedLinks.Add( link );
                    
                    // Track this cached result
                    var existingCached = cachedResults.FirstOrDefault( cr => cr.recordUri == cachedResult.Value.recordUri );
                    if (existingCached.result != null) {
                        existingCached.inputLinks.Add( link );
                    } else {
                        cachedResults.Add( (cachedResult.Value.result, cachedResult.Value.recordUri, new List<string> { link }) );
                    }

                    yield return cachedResult.Value.result;
                }
            }

            // If all links were cached, we're done
            if (processedLinks.Count == extractedLinks.Count) {
                yield break;
            }

            // Build content with only non-cached links for lookup
            var nonCachedLinks = extractedLinks.Except( processedLinks ).ToList( );
            if (nonCachedLinks.Count == 0) {
                yield break;
            }

            string contentWithNonCachedLinks = string.Join( " ", nonCachedLinks.Select( l => $"https://{l}" ) );

            // Perform lookup for non-cached links
            await foreach (MediaLinkResult result in _innerService.GetInfoAsync( contentWithNonCachedLinks )) {
                // Determine which input links generated this result
                var resultInputLinks = result._inputLinks.Select( l => l.Replace( "https://", "" ).Replace( "http://", "" ).TrimEnd( '/' ).ToLowerInvariant( ) ).ToList( );

                try {
                    // Cache the result with its input links
                    string recordUri = await _cacheService.CacheResultAsync( result, resultInputLinks );
                    _logger.LogInformation( "Cached new result to Bluesky: {uri}", recordUri );
                } catch (Exception ex) {
                    _logger.LogError( ex, "Failed to cache result, continuing without caching" );
                }

                yield return result;
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
