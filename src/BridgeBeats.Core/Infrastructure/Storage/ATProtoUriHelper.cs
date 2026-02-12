using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// Helper class for ATProto URI operations including validation and construction.
/// </summary>
public static class ATProtoUriHelper {

    /// <summary>
    /// The ATProto lexicon namespace for BridgeBeats lookup records.
    /// </summary>
    public const string LookupCollection = "link.bridgebeats.lookup";

    /// <summary>
    /// Validates that a DID (Decentralized Identifier) has a valid format.
    /// </summary>
    /// <remarks>
    /// Valid DIDs must start with "did:plc:" or "did:web:".
    /// Empty or null DIDs are considered valid (meaning ATProto is disabled).
    /// </remarks>
    /// <param name="did">The DID to validate.</param>
    /// <returns>True if the DID is valid or empty/null, false otherwise.</returns>
    public static bool IsValidDid( string? did ) {
        if (string.IsNullOrWhiteSpace( did )) {
            return true; // Empty/null means ATProto is disabled, which is valid
        }

        return did.StartsWith( "did:plc:", StringComparison.OrdinalIgnoreCase ) ||
               did.StartsWith( "did:web:", StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Validates that a DID has a valid format, throwing an exception if invalid.
    /// </summary>
    /// <param name="did">The DID to validate.</param>
    /// <param name="paramName">The parameter name to include in the exception message.</param>
    /// <exception cref="ArgumentException">Thrown when the DID has an invalid format.</exception>
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
    /// Builds an ATProto URI for a lookup record.
    /// </summary>
    /// <param name="did">The DID of the repository owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <returns>The complete AT URI for the lookup record.</returns>
    /// <exception cref="ArgumentException">Thrown when the DID has an invalid format.</exception>
    /// <exception cref="ArgumentNullException">Thrown when did or rkey is null/empty.</exception>
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
        if (includeInputLinkStrategy && result.InputLinks.Count > 0) {
            foreach (string inputLink in result.InputLinks) {
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
