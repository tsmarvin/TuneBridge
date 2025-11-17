using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// Service for temporarily storing MediaLinkResult objects for OpenGraph card generation.
    /// </summary>
    public interface IOpenGraphCardService {

        /// <summary>
        /// An indicator whether the OpenGraph card service is enabled.
        /// </summary>
        bool IsEnabled { get; }

        /// <summary>
        /// The base URL for the application (e.g., https://tunebridge.media).
        /// </summary>
        string BaseUrl { get; }

        /// <summary>
        /// Stores a MediaLinkResult and returns a unique identifier for it.
        /// </summary>
        /// <param name="result">The media link result to store.</param>
        /// <returns>A link to the stored card.</returns>
        string StoreResult( MediaLinkResult result );

        /// <summary>
        /// Retrieves a MediaLinkResult by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the result.</param>
        /// <returns>The media link result, or null if not found or expired.</returns>
        MediaLinkResult? GetResult( string id );

        /// <summary>
        /// Stores a collection of MediaLinkResult objects and returns a unique identifier for the collection.
        /// </summary>
        /// <param name="results">The collection of media link results to store.</param>
        /// <returns>A link to the stored multi-card page.</returns>
        string StoreMultipleResults( IEnumerable<MediaLinkResult> results );

        /// <summary>
        /// Retrieves a collection of MediaLinkResult objects by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the collection.</param>
        /// <returns>The collection of media link results, or null if not found or expired.</returns>
        IReadOnlyList<MediaLinkResult>? GetMultipleResults( string id );
    }
}
