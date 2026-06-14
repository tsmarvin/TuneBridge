using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Caches provider genre data (by track, by artist, and the track-to-artist mapping) and
/// backs the work queue that drives the artist genre-refresh worker.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>RedisGenreCache</c>
/// (<c>Infrastructure/Cache/RedisGenreCache.cs</c>). Per-provider genre data is stored without
/// normalization, for internal use only. Cache reads return <see langword="null"/> on a miss;
/// an empty list means the provider confirmed it has no genres for the item. The
/// enqueue/dequeue/length members form the artist refresh queue consumed by the Spotify genre
/// worker.
/// </remarks>
public interface IGenreCacheService {

    /// <summary>
    /// Reads the cached genres for a specific provider track. For Spotify, a miss may be resolved
    /// on demand by merging the cached genres of the track's artists.
    /// </summary>
    /// <param name="provider">The provider the id belongs to.</param>
    /// <param name="providerId">The provider-native track id.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the cached genre list, an empty list when the provider returned no
    /// genres, or <see langword="null"/> on a cache miss.
    /// </returns>
    Task<IReadOnlyList<string>?> GetGenresAsync(
        SupportedProviders provider,
        string providerId,
        CancellationToken ct = default );

    /// <summary>
    /// Stores the genres for a specific provider track in the cache. An empty collection records
    /// that the provider confirmed no genres are available.
    /// </summary>
    /// <param name="provider">The provider the id belongs to.</param>
    /// <param name="providerId">The provider-native track id.</param>
    /// <param name="genres">The genres to cache for the track.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the genres have been cached.</returns>
    Task SetGenresAsync(
        SupportedProviders provider,
        string providerId,
        IEnumerable<string> genres,
        CancellationToken ct = default );

    /// <summary>
    /// Reads the cached genres for a specific provider artist.
    /// </summary>
    /// <param name="provider">The provider the id belongs to.</param>
    /// <param name="artistId">The provider-native artist id.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the cached genre list, an empty list when the provider returned no
    /// genres, or <see langword="null"/> on a cache miss.
    /// </returns>
    Task<IReadOnlyList<string>?> GetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        CancellationToken ct = default );

    /// <summary>
    /// Stores the genres for a specific provider artist in the cache. An empty collection records
    /// that the provider confirmed no genres are available.
    /// </summary>
    /// <param name="provider">The provider the id belongs to.</param>
    /// <param name="artistId">The provider-native artist id.</param>
    /// <param name="genres">The genres to cache for the artist.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the genres have been cached.</returns>
    Task SetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        IEnumerable<string> genres,
        CancellationToken ct = default );

    /// <summary>
    /// Reads the cached list of artist ids associated with a track, used to resolve a
    /// track's genres via its artists.
    /// </summary>
    /// <param name="provider">The provider the id belongs to.</param>
    /// <param name="trackId">The provider-native track id.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the cached artist-id list for the track, or <see langword="null"/>
    /// on a cache miss.
    /// </returns>
    Task<IReadOnlyList<string>?> GetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        CancellationToken ct = default );

    /// <summary>
    /// Stores the mapping from a track to its artist ids in the cache.
    /// </summary>
    /// <param name="provider">The provider the id belongs to.</param>
    /// <param name="trackId">The provider-native track id.</param>
    /// <param name="artistIds">The artist ids associated with the track.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the mapping has been cached.</returns>
    Task SetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        IEnumerable<string> artistIds,
        CancellationToken ct = default );

    /// <summary>
    /// Adds artist ids to the refresh queue so a background worker can re-fetch their genres.
    /// Artist ids already in the queue are not duplicated.
    /// </summary>
    /// <param name="provider">The provider the artist ids belong to.</param>
    /// <param name="artistIds">The artist ids to enqueue for refresh.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the artist ids have been enqueued.</returns>
    Task EnqueueArtistsForRefreshAsync(
        SupportedProviders provider,
        IEnumerable<string> artistIds,
        CancellationToken ct = default );

    /// <summary>
    /// Removes and returns up to <paramref name="batchSize"/> artist ids from the refresh queue.
    /// </summary>
    /// <param name="provider">The provider whose refresh queue is drained.</param>
    /// <param name="batchSize">The maximum number of artist ids to dequeue in this batch.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the dequeued artist ids; an empty list when the queue is empty.
    /// </returns>
    Task<IReadOnlyList<string>> DequeueArtistsForRefreshAsync(
        SupportedProviders provider,
        int batchSize,
        CancellationToken ct = default );

    /// <summary>
    /// Reports the current number of artist ids waiting in the refresh queue.
    /// </summary>
    /// <param name="provider">The provider whose refresh queue is measured.</param>
    /// <param name="ct">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the number of queued artist ids.</returns>
    Task<long> GetArtistRefreshQueueLengthAsync(
        SupportedProviders provider,
        CancellationToken ct = default );
}
