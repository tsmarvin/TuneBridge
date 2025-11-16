using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Interfaces {

    /// <summary>
    /// Service for caching MediaLinkResult lookups using SQLite and ATProto PDS.
    /// </summary>
    public interface IMediaLinkCacheRepository {

        /// <summary>
        /// Attempts to get a cached MediaLinkResult by input link.
        /// </summary>
        /// <param name="inputLink">The input link to search for.</param>
        /// <returns>A tuple containing the cached result, its ATProto record URI, and staleness indicator, or null if not found.</returns>
        Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink );

        /// <summary>
        /// Attempts to get a cached MediaLinkResult by ISRC (International Standard Recording Code).
        /// </summary>
        /// <param name="isrc">The ISRC code to search for.</param>
        /// <returns>A tuple containing the cached result, its ATProto record URI, and staleness indicator, or null if not found.</returns>
        Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByISRCAsync( string isrc );

        /// <summary>
        /// Attempts to get a cached MediaLinkResult by UPC (Universal Product Code).
        /// </summary>
        /// <param name="upc">The UPC code to search for.</param>
        /// <returns>A tuple containing the cached result, its ATProto record URI, and staleness indicator, or null if not found.</returns>
        Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByUPCAsync( string upc );

        /// <summary>
        /// Attempts to get a cached MediaLinkResult by title and artist metadata.
        /// </summary>
        /// <param name="title">The track or album title.</param>
        /// <param name="artist">The artist name.</param>
        /// <returns>A tuple containing the cached result, its ATProto record URI, and staleness indicator, or null if not found.</returns>
        Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByMetadataAsync( string title, string artist );

        /// <summary>
        /// Stores a MediaLinkResult in the cache and on ATProto PDS.
        /// </summary>
        /// <param name="result">The MediaLinkResult to cache.</param>
        /// <param name="inputLinks">The input links that generated this result.</param>
        /// <returns>The ATProto record URI.</returns>
        Task<string> CacheResultAsync( MediaLinkResult result, IEnumerable<string> inputLinks );

        /// <summary>
        /// Updates an existing cache entry with a fresh lookup result.
        /// </summary>
        /// <param name="recordUri">The ATProto record URI of the existing cache entry.</param>
        /// <param name="result">The updated MediaLinkResult.</param>
        /// <param name="inputLinks">All input links to associate with this result.</param>
        Task UpdateCacheEntryAsync( string recordUri, MediaLinkResult result, IEnumerable<string> inputLinks );

        /// <summary>
        /// Adds additional input links to an existing cache entry.
        /// </summary>
        /// <param name="recordUri">The ATProto record URI of the cache entry.</param>
        /// <param name="newLinks">The new input links to associate with the cache entry.</param>
        Task AddInputLinksAsync( string recordUri, IEnumerable<string> newLinks );
    }
}
