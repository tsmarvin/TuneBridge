using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Persists and retrieves cross-provider lookup results as records in the AT Protocol PDS,
/// and enumerates all stored results for a user.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>ATProtoStorageService</c>
/// (<c>Infrastructure/Storage/ATProtoStorageService.cs</c>). Record identities are AT-URIs
/// (<c>at://…</c>); the in-memory shape stored here is <see cref="MediaLinkResult"/>, whose
/// at-rest twin is the <c>MediaLinkResultRecord</c> PDS record.
/// </remarks>
public interface IATProtoStorageService {

    /// <summary>
    /// Stores a media-link result as a PDS record and returns the AT-URI of the created record.
    /// The record key is derived deterministically from the result, so storing the same result
    /// again updates the existing record (upsert) rather than creating a duplicate.
    /// </summary>
    /// <param name="result">The in-memory <see cref="MediaLinkResult"/> to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the AT-URI (<c>at://…</c>) of the stored record.</returns>
    Task<string> StoreMediaLinkResultAsync( MediaLinkResult result, CancellationToken cancellationToken = default );

    /// <summary>
    /// Retrieves a previously stored media-link result by its record AT-URI.
    /// </summary>
    /// <param name="recordUri">The AT-URI (<c>at://…</c>) of the record to read.</param>
    /// <returns>
    /// A task whose result is the stored <see cref="MediaLinkResult"/>, or <see langword="null"/>
    /// when no record exists at the given AT-URI.
    /// </returns>
    Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri );

    /// <summary>
    /// Streams every stored media-link record for a given PDS and user, pairing each record's
    /// AT-URI with its deserialized result. Issues a single HTTP GET to
    /// <c>com.atproto.sync.getRepo</c> and parses the resulting CAR file locally rather than
    /// making paginated <c>listRecords</c> calls.
    /// </summary>
    /// <param name="pdsUri">The base URI of the PDS to read from.</param>
    /// <param name="userDid">The DID of the user whose records are enumerated.</param>
    /// <param name="cancellationToken">Token used to stop enumeration.</param>
    /// <param name="forceRefresh">
    /// When <see langword="true"/>, bypasses the TTL cache and forces a fresh download under the
    /// single-flight lock. Use for admin-triggered manual refreshes.
    /// </param>
    /// <returns>
    /// An asynchronous sequence of tuples, each carrying a record's AT-URI and its
    /// <see cref="MediaLinkResult"/>. Used by statistics aggregation.
    /// </returns>
    /// <remarks>
    /// Throws on download failure or CAR structural errors; per-record deserialization failures
    /// are skipped with a warning log rather than aborting the enumeration.
    /// </remarks>
    IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> ListAllRecordsAsync(
        Uri pdsUri,
        string userDid,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false
    );

}
