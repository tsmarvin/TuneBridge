using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// Service for storing and retrieving MediaLinkResult records on Bluesky PDS.
    /// </summary>
    public interface IBlueskyStorageService {

        /// <summary>
        /// Stores a MediaLinkResult as a custom lexicon record on Bluesky PDS.
        /// </summary>
        /// <param name="result">The MediaLinkResult to store.</param>
        /// <returns>The AT-URI of the created record.</returns>
        Task<string> StoreMediaLinkResultAsync( MediaLinkResult result );

        /// <summary>
        /// Retrieves a MediaLinkResult from Bluesky PDS by its AT-URI.
        /// </summary>
        /// <param name="recordUri">The AT-URI of the record.</param>
        /// <returns>The MediaLinkResult, or null if not found.</returns>
        Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri );

        /// <summary>
        /// Updates an existing MediaLinkResult record on Bluesky PDS.
        /// </summary>
        /// <param name="recordUri">The AT-URI of the record to update.</param>
        /// <param name="result">The updated MediaLinkResult.</param>
        /// <returns>True if the update was successful, false otherwise.</returns>
        Task<bool> UpdateMediaLinkResultAsync( string recordUri, MediaLinkResult result );
    }
}
