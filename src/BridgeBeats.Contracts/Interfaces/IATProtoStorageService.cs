using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for storing and retrieving MediaLinkResult records on ATProto PDS.
/// </summary>
public interface IATProtoStorageService {

    /// <summary>
    /// Stores a MediaLinkResult as a custom lexicon record on ATProto PDS.
    /// Uses a deterministic rkey based on the externalId (ISRC/UPC) and media type.
    /// If a record with the same rkey exists, it will be updated (upsert).
    /// </summary>
    /// <param name="result">The MediaLinkResult to store.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The AT-URI of the created or updated record.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no externalId is found in the result.</exception>
    Task<string> StoreMediaLinkResultAsync( MediaLinkResult result, CancellationToken cancellationToken = default );

    /// <summary>
    /// Retrieves a MediaLinkResult from ATProto PDS by its AT-URI.
    /// </summary>
    /// <param name="recordUri">The AT-URI of the record.</param>
    /// <returns>The MediaLinkResult, or null if not found.</returns>
    Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri );

    /// <summary>
    /// Lists all MediaLinkResult records from the ATProto PDS collection.
    /// Uses unauthenticated access for public records with cursor-based pagination.
    /// </summary>
    /// <param name="pdsUri">The PDS URI to query (e.g., "https://pds.bridgebeats.link").</param>
    /// <param name="userDid">The DID of the account whose collection to query.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>An async enumerable of tuples containing the AT-URI and MediaLinkResult for each record.</returns>
    IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> ListAllRecordsAsync(
        Uri pdsUri,
        string userDid,
        CancellationToken cancellationToken = default
    );

}
