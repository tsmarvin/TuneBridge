using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

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

        /// <summary>
        /// Retrieves a MediaLinkResult from ATProto PDS by its rkey.
        /// </summary>
        /// <param name="rkey">The record key (e.g., "track:USRC12345678" or "album:123456789012").</param>
        /// <returns>The MediaLinkResult, or null if not found.</returns>
        Task<MediaLinkResult?> GetMediaLinkResultByRkeyAsync( string rkey );

        /// <summary>
        /// Updates an existing MediaLinkResult record on ATProto PDS.
        /// </summary>
        /// <param name="recordUri">The AT-URI of the record to update.</param>
        /// <param name="result">The updated MediaLinkResult.</param>
        /// <returns>True if the update was successful, false otherwise.</returns>
        Task<bool> UpdateMediaLinkResultAsync( string recordUri, MediaLinkResult result );
    }
}

