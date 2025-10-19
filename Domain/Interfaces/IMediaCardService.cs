using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// Service for storing and retrieving MediaLinkResult instances for OpenGraph card generation.
    /// </summary>
    public interface IMediaCardService {

        /// <summary>
        /// Stores a MediaLinkResult and returns a unique identifier for retrieving it later.
        /// </summary>
        /// <param name="result">The MediaLinkResult to store.</param>
        /// <returns>A unique identifier (GUID) for the stored result.</returns>
        string StoreResult( MediaLinkResult result );

        /// <summary>
        /// Retrieves a MediaLinkResult by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier for the result.</param>
        /// <returns>The MediaLinkResult if found, null otherwise.</returns>
        MediaLinkResult? GetResult( string id );
    }

}
