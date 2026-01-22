using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for caching music genre data from various providers.
/// Stores per-provider genre data without normalization for internal use only.
/// </summary>
/// <remarks>
/// Genre data is cached opportunistically during natural lookup flows.
/// Data persists until explicitly overwritten (no TTL expiration).
/// </remarks>
public interface IGenreCacheService {

    /// <summary>
    /// Gets cached genres for a track by provider and provider-specific ID.
    /// For Spotify, this may resolve genres on-demand by merging cached artist genres.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="providerId">The provider-specific track ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of genre names, empty array if provider returned no genres, or null if not cached.</returns>
    Task<IReadOnlyList<string>?> GetGenresAsync(
        SupportedProviders provider,
        string providerId,
        CancellationToken ct = default );

    /// <summary>
    /// Caches genres for a track. An empty array indicates the provider confirmed no genres are available.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="providerId">The provider-specific track ID.</param>
    /// <param name="genres">The genre names to cache. Empty collection means no genres available.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetGenresAsync(
        SupportedProviders provider,
        string providerId,
        IEnumerable<string> genres,
        CancellationToken ct = default );

    /// <summary>
    /// Gets cached genres for an artist by provider and artist ID.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="artistId">The provider-specific artist ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of genre names, empty array if provider returned no genres, or null if not cached.</returns>
    Task<IReadOnlyList<string>?> GetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        CancellationToken ct = default );

    /// <summary>
    /// Caches genres for an artist. An empty array indicates the provider confirmed no genres are available.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="artistId">The provider-specific artist ID.</param>
    /// <param name="genres">The genre names to cache. Empty collection means no genres available.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetArtistGenresAsync(
        SupportedProviders provider,
        string artistId,
        IEnumerable<string> genres,
        CancellationToken ct = default );

    /// <summary>
    /// Gets the track-to-artist mapping for a track. Used for Spotify genre resolution.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="trackId">The provider-specific track ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of artist IDs associated with the track, or null if not cached.</returns>
    Task<IReadOnlyList<string>?> GetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        CancellationToken ct = default );

    /// <summary>
    /// Caches the track-to-artist mapping for a track.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="trackId">The provider-specific track ID.</param>
    /// <param name="artistIds">The artist IDs associated with the track.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetTrackArtistMappingAsync(
        SupportedProviders provider,
        string trackId,
        IEnumerable<string> artistIds,
        CancellationToken ct = default );

    /// <summary>
    /// Adds artist IDs to the refresh queue for batch genre fetching.
    /// Artists already in the queue are not duplicated.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="artistIds">The artist IDs to enqueue for genre refresh.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnqueueArtistsForRefreshAsync(
        SupportedProviders provider,
        IEnumerable<string> artistIds,
        CancellationToken ct = default );

    /// <summary>
    /// Removes and returns artist IDs from the refresh queue for batch processing.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="batchSize">Maximum number of artist IDs to dequeue.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of artist IDs ready for genre fetching.</returns>
    Task<IReadOnlyList<string>> DequeueArtistsForRefreshAsync(
        SupportedProviders provider,
        int batchSize,
        CancellationToken ct = default );

    /// <summary>
    /// Gets the current length of the artist refresh queue for a provider.
    /// </summary>
    /// <param name="provider">The music provider.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of artists waiting in the refresh queue.</returns>
    Task<long> GetArtistRefreshQueueLengthAsync(
        SupportedProviders provider,
        CancellationToken ct = default );
}
