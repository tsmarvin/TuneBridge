using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for retrieving statistics about the lookup collection.
/// </summary>
public interface IStatisticsService {

    /// <summary>
    /// Gets the most recently cached statistics, or null if none have been computed yet.
    /// Used by the controller to distinguish "no data yet" from "data available".
    /// </summary>
    /// <returns>The cached lookup statistics, or null if no data has been computed.</returns>
    LookupStatistics? GetCachedStatistics( );

    /// <summary>
    /// Returns true if a background refresh is currently in progress.
    /// </summary>
    bool IsRefreshing { get; }

    /// <summary>
    /// Triggers a background refresh via Channel signal. Returns immediately.
    /// Returns true if a refresh signal was sent, false if one is already in progress.
    /// </summary>
    /// <returns>True if a refresh was triggered; false if one is already running.</returns>
    bool TriggerRefresh( );

    /// <summary>
    /// Gets aggregated statistics. Returns cached data if available, otherwise an empty
    /// LookupStatistics (TotalRecords=0, GeneratedAt=MinValue). Never blocks, never throws.
    /// Exists for backward compatibility — new code should use <see cref="GetCachedStatistics"/> instead.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The lookup statistics.</returns>
    Task<LookupStatistics> GetStatisticsAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Forces a synchronous refresh of the cached statistics. Called only by the
    /// background service internally. Acquires the semaphore and computes fresh stats.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The refreshed lookup statistics.</returns>
    Task<LookupStatistics> RefreshStatisticsAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Reads the current cache bootstrap status directly from Redis, bypassing the
    /// statistics cache. Returns null if the status is absent or cannot be read.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The live bootstrap status, or null if unavailable.</returns>
    Task<CacheBootstrapStatus?> GetLiveBootstrapStatusAsync( CancellationToken cancellationToken = default );
}
