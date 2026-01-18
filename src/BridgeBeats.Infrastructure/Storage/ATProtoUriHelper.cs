using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;

namespace BridgeBeats.Infrastructure.Storage;

/// <summary>
/// Helper class for retrieving ATProto URIs from cache.
/// </summary>
public static class ATProtoUriHelper {

    /// <summary>
    /// Attempts to retrieve the ATProto URI for a MediaLinkResult from the cache.
    /// Tries multiple lookup strategies: input link, external ID (ISRC/UPC), and metadata.
    /// </summary>
    /// <param name="result">The MediaLinkResult to find in cache.</param>
    /// <param name="cacheRepository">The cache repository to query.</param>
    /// <param name="includeInputLinkStrategy">Whether to include input link lookup strategy (default true).</param>
    /// <returns>The ATProto URI if found in cache, otherwise null.</returns>
    public static async Task<string?> GetATProtoUriFromCacheAsync(
        MediaLinkResult result,
        IMediaLinkCacheRepository? cacheRepository,
        bool includeInputLinkStrategy = true ) {

        if (cacheRepository == null || result == null) {
            return null;
        }

        // Strategy 1: Try to find by input link if available
        if (includeInputLinkStrategy && result._inputLinks.Count > 0) {
            foreach (string inputLink in result._inputLinks) {
                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await cacheRepository.TryGetCachedResultAsync( inputLink );
                if (cachedResult.HasValue) {
                    return cachedResult.Value.recordUri;
                }
            }
        }

        // Strategy 2: Try to find by external ID (ISRC or UPC)
        MusicLookupResult? firstResultWithId = result.Results.Values
            .FirstOrDefault( r => !string.IsNullOrWhiteSpace( r.ExternalId ) );

        if (firstResultWithId != null) {
            if (firstResultWithId.IsAlbum == true) {
                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await cacheRepository.TryGetCachedResultByUPCAsync( firstResultWithId.ExternalId );
                if (cachedResult.HasValue) {
                    return cachedResult.Value.recordUri;
                }
            } else {
                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await cacheRepository.TryGetCachedResultByISRCAsync( firstResultWithId.ExternalId );
                if (cachedResult.HasValue) {
                    return cachedResult.Value.recordUri;
                }
            }
        }

        // Strategy 3: Try to find by metadata (title and artist)
        MusicLookupResult? firstResult = result.Results.Values.FirstOrDefault( );
        if (firstResult != null &&
            !string.IsNullOrWhiteSpace( firstResult.Title ) &&
            !string.IsNullOrWhiteSpace( firstResult.Artist )) {
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await cacheRepository.TryGetCachedResultByMetadataAsync(
                firstResult.Title,
                firstResult.Artist );
            if (cachedResult.HasValue) {
                return cachedResult.Value.recordUri;
            }
        }

        return null;
    }
}
