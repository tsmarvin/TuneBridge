using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// Service for temporarily storing MediaLinkResult objects for OpenGraph card generation.
    /// </summary>
    public interface IOpenGraphCardService {
        
        /// <summary>
        /// Stores a MediaLinkResult and returns a unique identifier for it.
        /// </summary>
        /// <param name="result">The media link result to store.</param>
        /// <returns>A unique identifier that can be used to retrieve the result.</returns>
        string StoreResult( MediaLinkResult result );

        /// <summary>
        /// Retrieves a MediaLinkResult by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the result.</param>
        /// <returns>The media link result, or null if not found or expired.</returns>
        MediaLinkResult? GetResult( string id );
    }
}
