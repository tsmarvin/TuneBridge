using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces;

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
    /// Attempts to get a cached MediaLinkResult by provider-specific ID.
    /// </summary>
    /// <param name="providerId">The provider-specific identifier (e.g., Apple Music catalog ID, Spotify track/album ID).</param>
    /// <param name="provider">The music provider that the ID belongs to.</param>
    /// <param name="isAlbum">True to look up an album, false to look up a track.</param>
    /// <returns>A tuple containing the cached result, its ATProto record URI, and staleness indicator, or null if not found.</returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum );

    /// <summary>
    /// Attempts to get a cached MediaLinkResult by its deterministic card ID.
    /// </summary>
    /// <param name="cardId">The card ID (hash-based, URL-safe identifier used in /card/{id} endpoints).</param>
    /// <returns>A tuple containing the cached result, its ATProto record URI, and staleness indicator, or null if not found.</returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByCardIdAsync( string cardId );

    /// <summary>
    /// Stores or updates (upserts) a MediaLinkResult in the cache and on ATProto PDS.
    /// </summary>
    /// <param name="result">The MediaLinkResult to cache.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The ATProto record URI.</returns>
    Task<string> CacheResultAsync( MediaLinkResult result, CancellationToken cancellationToken = default );

    /// <summary>
    /// Adds additional input links to an existing cache entry.
    /// </summary>
    /// <param name="recordUri">The ATProto record URI of the cache entry.</param>
    /// <param name="result">The result object containing the new input links to associate with the cache entry.</param>
    Task AddInputLinksAsync( string recordUri, MediaLinkResult result );
}
