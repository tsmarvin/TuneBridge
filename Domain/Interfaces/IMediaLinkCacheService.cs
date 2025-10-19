using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// Service for caching MediaLinkResult lookups using SQLite and Bluesky PDS.
    /// </summary>
    public interface IMediaLinkCacheService {

        /// <summary>
        /// Attempts to get a cached MediaLinkResult by input link.
        /// </summary>
        /// <param name="inputLink">The input link to search for.</param>
        /// <returns>A tuple containing the cached result and its Bluesky record URI, or null if not found or expired.</returns>
        Task<(MediaLinkResult result, string recordUri)?> TryGetCachedResultAsync( string inputLink );

        /// <summary>
        /// Stores a MediaLinkResult in the cache and on Bluesky PDS.
        /// </summary>
        /// <param name="result">The MediaLinkResult to cache.</param>
        /// <param name="inputLinks">The input links that generated this result.</param>
        /// <returns>The Bluesky record URI.</returns>
        Task<string> CacheResultAsync( MediaLinkResult result, IEnumerable<string> inputLinks );

        /// <summary>
        /// Adds additional input links to an existing cache entry.
        /// </summary>
        /// <param name="recordUri">The Bluesky record URI of the cache entry.</param>
        /// <param name="newLinks">The new input links to associate with the cache entry.</param>
        Task AddInputLinksAsync( string recordUri, IEnumerable<string> newLinks );
    }
}
