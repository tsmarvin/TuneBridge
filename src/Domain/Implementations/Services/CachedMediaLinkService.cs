using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.LinkParsers;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Decorator for <see cref="IMediaLinkService"/> that adds caching with ATProto PDS storage.
    /// This service checks the cache before performing lookups and stores results for future use.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="CachingMediaLinkService"/> class.
    /// </remarks>
    /// <param name="enabledProvidersCollection">Dictionary of active provider services keyed by provider type.</param>
    /// <param name="cacheRepository">The cache repository for storing and retrieving results.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="serializerOptions">JSON serialization options for logging.</param>
    public partial class CachingMediaLinkService(
        Dictionary<SupportedProviders, IMusicLookupService> enabledProvidersCollection,
        IMediaLinkCacheRepository cacheRepository,
        ILogger<CachingMediaLinkService> logger,
        JsonSerializerOptions serializerOptions
    ) : MediaLinkServiceBase( enabledProvidersCollection, logger, serializerOptions ) {

        private readonly IMediaLinkCacheRepository _cacheRepository = cacheRepository;

        // Static compiled regex for URL extraction (performance optimization)
        private static readonly Regex s_urlRegex = UrlRegex( );

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoAsync( string title, string artist ) {
            // Check cache first
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                await _cacheRepository.TryGetCachedResultByMetadataAsync( title, artist );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for title/artist lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            // Perform fresh lookup using base class helper methods
            (MusicLookupResultDto result, SupportedProviders provider)? lookupResult =
                await GetMusicLookupResults( title, artist );
            MediaLinkResult? result = await CombineLookupInfoAsync( lookupResult );

            if (result is null) {
                return null;
            }

            try {
                if (cachedResult.HasValue && cachedResult.Value.isStale) {
                    // Update existing stale entry
                    await _cacheRepository.UpdateCacheEntryAsync( cachedResult.Value.recordUri, result, [] );
                    Logger.LogInformation( "Refreshed stale cache entry: {uri} for title/artist lookup", cachedResult.Value.recordUri );
                } else {
                    // Cache as new result
                    string recordUri = await _cacheRepository.CacheResultAsync( result, [] );
                    Logger.LogInformation( "Cached new title/artist lookup result to ATProto: {uri}", recordUri );
                }
            } catch (DbUpdateException ex) {
                Logger.LogError( ex, "Database error while caching title/artist lookup result, continuing without caching" );
            } catch (HttpRequestException ex) {
                Logger.LogError( ex, "HTTP error while caching title/artist lookup result, continuing without caching" );
            } catch (InvalidOperationException ex) {
                Logger.LogError( ex, "Invalid operation while caching title/artist lookup result, continuing without caching" );
            }

            return result;
        }

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc ) {
            // Check cache first
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                await _cacheRepository.TryGetCachedResultByISRCAsync( isrc );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for ISRC lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            // Perform fresh lookup using base class helper methods
            (MusicLookupResultDto result, SupportedProviders provider)? lookupResult =
                await GetMusicLookupResults( isrc, false );
            MediaLinkResult? result = await CombineLookupInfoAsync( lookupResult );

            if (result is null) {
                return null;
            }

            try {
                if (cachedResult.HasValue && cachedResult.Value.isStale) {
                    // Update existing stale entry
                    await _cacheRepository.UpdateCacheEntryAsync( cachedResult.Value.recordUri, result, [] );
                    Logger.LogInformation( "Refreshed stale cache entry: {uri} for ISRC lookup", cachedResult.Value.recordUri );
                } else {
                    // Cache as new result
                    string recordUri = await _cacheRepository.CacheResultAsync( result, [] );
                    Logger.LogInformation( "Cached new ISRC lookup result to ATProto: {uri}", recordUri );
                }
            } catch (DbUpdateException ex) {
                Logger.LogError( ex, "Database error while caching ISRC lookup result, continuing without caching" );
            } catch (HttpRequestException ex) {
                Logger.LogError( ex, "HTTP error while caching ISRC lookup result, continuing without caching" );
            } catch (InvalidOperationException ex) {
                Logger.LogError( ex, "Invalid operation while caching ISRC lookup result, continuing without caching" );
            }

            return result;
        }

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc ) {
            // Check cache first
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                await _cacheRepository.TryGetCachedResultByUPCAsync( upc );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for UPC lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            // Perform fresh lookup using base class helper methods
            (MusicLookupResultDto result, SupportedProviders provider)? lookupResult =
                await GetMusicLookupResults( upc, true );
            MediaLinkResult? result = await CombineLookupInfoAsync( lookupResult );

            if (result is null) {
                return null;
            }

            try {
                if (cachedResult.HasValue && cachedResult.Value.isStale) {
                    // Update existing stale entry
                    await _cacheRepository.UpdateCacheEntryAsync( cachedResult.Value.recordUri, result, [] );
                    Logger.LogInformation( "Refreshed stale cache entry: {uri} for UPC lookup", cachedResult.Value.recordUri );
                } else {
                    // Cache as new result
                    string recordUri = await _cacheRepository.CacheResultAsync( result, [] );
                    Logger.LogInformation( "Cached new UPC lookup result to ATProto: {uri}", recordUri );
                }
            } catch (DbUpdateException ex) {
                Logger.LogError( ex, "Database error while caching UPC lookup result, continuing without caching" );
            } catch (HttpRequestException ex) {
                Logger.LogError( ex, "HTTP error while caching UPC lookup result, continuing without caching" );
            } catch (InvalidOperationException ex) {
                Logger.LogError( ex, "Invalid operation while caching UPC lookup result, continuing without caching" );
            }

            return result;
        }

        /// <inheritdoc/>
        public override async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
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

                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await _cacheRepository.TryGetCachedResultAsync( link );
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
                        Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri}", cachedResult.Value.recordUri );

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

                // Use base class helper methods for fresh lookups
                Dictionary<MusicLookupResultDto, (SupportedProviders provider, string inputLink)> linkResults =
                    await GetMusicLookupResults( contentWithNonCachedLinks );

                await foreach (MediaLinkResult result in CombineLookupInfoAsync( linkResults )) {
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
                            await _cacheRepository.UpdateCacheEntryAsync( matchingStaleRecordUri, result, resultInputLinks );
                            Logger.LogInformation( "Refreshed stale cache entry: {uri}", matchingStaleRecordUri );

                            // Remove from stale entries to avoid reprocessing
                            _ = staleEntries.Remove( matchingStaleRecordUri );
                        } else if (matchingFreshRecordUri != null) {
                            // Add new links to existing fresh cache entry
                            await _cacheRepository.AddInputLinksAsync( matchingFreshRecordUri, resultInputLinks );
                            Logger.LogInformation( "Added new links to existing fresh cache entry: {uri}", matchingFreshRecordUri );
                        } else {
                            // Cache as a new result
                            string recordUri = await _cacheRepository.CacheResultAsync( result, resultInputLinks );
                            Logger.LogInformation( "Cached new result to ATProto: {uri}", recordUri );
                        }
                    } catch (DbUpdateException ex) {
                        Logger.LogError( ex, "Database error while caching result, continuing without caching" );
                    } catch (HttpRequestException ex) {
                        Logger.LogError( ex, "HTTP error while caching result, continuing without caching" );
                    } catch (InvalidOperationException ex) {
                        Logger.LogError( ex, "Invalid operation while caching result, continuing without caching" );
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

            foreach (Match match in matches) {
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
