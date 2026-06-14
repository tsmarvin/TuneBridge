using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Serves aggregate lookup statistics with a cached value and a background refresh, plus the
/// live cache-bootstrap worker status that can be merged into the stats.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>StatisticsService</c>
/// (<c>Domain/Services/Statistics/StatisticsService.cs</c>). <see cref="GetCachedStatistics"/>
/// and <see cref="TriggerRefresh"/> are non-blocking; the <c>Async</c> methods may compute or
/// recompute the statistics.
/// </remarks>
public interface IStatisticsService {

    /// <summary>
    /// Returns the most recently cached statistics without computing or waiting. Lets a caller
    /// distinguish "no data yet" from "data available".
    /// </summary>
    /// <returns>
    /// The cached <see cref="LookupStatistics"/>, or <see langword="null"/> when nothing has been
    /// computed yet.
    /// </returns>
    LookupStatistics? GetCachedStatistics( );

    /// <summary>
    /// Whether a background statistics refresh is currently in progress.
    /// </summary>
    bool IsRefreshing { get; }

    /// <summary>
    /// Requests a background refresh of the statistics via a channel signal and returns
    /// immediately (fire-and-forget).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when a refresh signal was sent by this call; <see langword="false"/>
    /// when one was already running and no new refresh was started.
    /// </returns>
    bool TriggerRefresh( );

    /// <summary>
    /// Returns the current statistics, serving the cached value when available and otherwise an
    /// empty <see cref="LookupStatistics"/> (zero records, <c>GeneratedAt</c> at
    /// <see cref="System.DateTimeOffset.MinValue"/>). Never blocks and never throws. Retained for
    /// backward compatibility; new code should prefer <see cref="GetCachedStatistics"/>.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the current <see cref="LookupStatistics"/>.</returns>
    Task<LookupStatistics> GetStatisticsAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Acquires the refresh lock, recomputes the statistics from source, updates the cache, and
    /// returns the fresh value. Called internally by the background service.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the freshly computed <see cref="LookupStatistics"/>.</returns>
    Task<LookupStatistics> RefreshStatisticsAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Reads the current cache-bootstrap worker status directly from Redis, bypassing the
    /// statistics cache. The status can be merged into the statistics projection.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A task whose result is the live <see cref="CacheBootstrapStatus"/>, or <see langword="null"/>
    /// when the status is absent or cannot be read.
    /// </returns>
    Task<CacheBootstrapStatus?> GetLiveBootstrapStatusAsync( CancellationToken cancellationToken = default );
}
