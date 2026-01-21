using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Service for retrieving statistics about the lookup collection.
/// </summary>
public interface IStatisticsService {

    /// <summary>
    /// Gets aggregated statistics about the lookup collection.
    /// Results are cached for efficiency (default: 6 hours).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The lookup statistics.</returns>
    Task<LookupStatistics> GetStatisticsAsync( CancellationToken cancellationToken = default );

    /// <summary>
    /// Forces a refresh of the cached statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The refreshed lookup statistics.</returns>
    Task<LookupStatistics> RefreshStatisticsAsync( CancellationToken cancellationToken = default );
}
