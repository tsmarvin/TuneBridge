using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// Helpers for validating decentralized identifiers (DIDs) and building and resolving AT-URIs of the
/// form <c>at://{did}/{collection}/{rkey}</c> for the BridgeBeats lookup collection.
/// </summary>
public static class ATProtoUriHelper {

    /// <summary>
    /// The collection NSID under which BridgeBeats media-link lookup records are stored.
    /// </summary>
    public const string LookupCollection = "link.bridgebeats.lookup";

    /// <summary>
    /// Determines whether a DID has a recognized, well-formed value.
    /// </summary>
    /// <param name="did">The DID to check.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="did"/> is null, empty, or whitespace (a DID is
    /// optional, so "no DID" is treated as valid), or when it begins with <c>did:plc:</c> or
    /// <c>did:web:</c>; otherwise <see langword="false"/>. Only a non-empty, malformed DID is rejected.
    /// </returns>
    public static bool IsValidDid( string? did ) {
        if (string.IsNullOrWhiteSpace( did )) {
            return true; // Empty/null means ATProto is disabled, which is valid
        }

        return did.StartsWith( "did:plc:", StringComparison.OrdinalIgnoreCase ) ||
               did.StartsWith( "did:web:", StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Throws when a DID is non-empty but malformed; a null or empty DID passes validation.
    /// </summary>
    /// <param name="did">The DID to validate.</param>
    /// <param name="paramName">The parameter name to attribute the exception to.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="did"/> is non-empty and does not start with <c>did:plc:</c> or <c>did:web:</c>.
    /// </exception>
    public static void ValidateDid( string? did, string paramName = "did" ) {
        if (!IsValidDid( did )) {
            throw new ArgumentException(
                $"Invalid DID format: '{did}'. DIDs must start with 'did:plc:' or 'did:web:' prefix. " +
                "Example: did:plc:abc123xyz or did:web:example.com",
                paramName
            );
        }
    }

    /// <summary>
    /// Builds the AT-URI for a record in the lookup collection: <c>at://{did}/{collection}/{rkey}</c>.
    /// </summary>
    /// <param name="did">The repository DID; must be non-empty and well-formed.</param>
    /// <param name="rkey">The record key; must be non-empty.</param>
    /// <returns>The fully qualified AT-URI for the lookup record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="did"/> or <paramref name="rkey"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="did"/> is non-empty but malformed.</exception>
    public static string BuildLookupRecordUri( string did, string rkey ) {
        if (string.IsNullOrWhiteSpace( did )) {
            throw new ArgumentNullException( nameof( did ), "DID cannot be null or empty when building a record URI." );
        }

        if (string.IsNullOrWhiteSpace( rkey )) {
            throw new ArgumentNullException( nameof( rkey ), "Record key cannot be null or empty." );
        }

        ValidateDid( did, nameof( did ) );

        return $"at://{did}/{LookupCollection}/{rkey}";
    }

    /// <summary>
    /// Resolves the stored AT-URI for a media-link result by probing the cache with several lookup
    /// strategies in order, returning the first hit.
    /// </summary>
    /// <param name="result">The media-link result whose record URI is sought.</param>
    /// <param name="cacheRepository">The cache repository to query; a null repository yields a null result.</param>
    /// <param name="includeInputLinkStrategy">
    /// When <see langword="true"/>, the first strategy probes the cache by each of the result's input
    /// links before falling back to external-id and metadata strategies.
    /// </param>
    /// <returns>
    /// The cached record URI from the first matching strategy &#8212; input link, then external id
    /// (UPC for an album, ISRC for a track), then title and artist metadata &#8212; or
    /// <see langword="null"/> when nothing matches or either argument is null.
    /// </returns>
    public static async Task<string?> GetATProtoUriFromCacheAsync(
        MediaLinkResult result,
        IMediaLinkCacheRepository? cacheRepository,
        bool includeInputLinkStrategy = true ) {

        if (cacheRepository == null || result == null) {
            return null;
        }

        // Try to find by input link if available
        if (includeInputLinkStrategy && result.InputLinks.Count > 0) {
            foreach (string inputLink in result.InputLinks) {
                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await cacheRepository.TryGetCachedResultAsync( inputLink );
                if (cachedResult.HasValue) {
                    return cachedResult.Value.recordUri;
                }
            }
        }

        // Try to find by external ID (ISRC or UPC)
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

        // Try to find by metadata (title and artist)
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
