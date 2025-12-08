using BridgeBeats.Domain.Contracts.DTOs;

namespace BridgeBeats.Domain.Interfaces {

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
        /// <returns>The AT-URI of the created or updated record.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no externalId is found in the result.</exception>
        Task<string> StoreMediaLinkResultAsync( MediaLinkResult result );

        /// <summary>
        /// Retrieves a MediaLinkResult from ATProto PDS by its AT-URI.
        /// </summary>
        /// <param name="recordUri">The AT-URI of the record.</param>
        /// <returns>The MediaLinkResult, or null if not found.</returns>
        Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri );

    }
}
