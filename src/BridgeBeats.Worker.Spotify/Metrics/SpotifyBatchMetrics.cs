using System.Diagnostics.Metrics;

namespace BridgeBeats.Worker.Spotify.Metrics;

/// <summary>
/// OpenTelemetry metrics for Spotify batch (bulk) lookup processing.
/// </summary>
/// <remarks>
/// <para>
/// Register this meter with OpenTelemetry using
/// <c>.AddMeter("BridgeBeats.Spotify.Batch")</c> in the Spotify worker's telemetry configuration.
/// </para>
/// <para>
/// These metrics complement the HTTP-level endpoint metrics already captured by
/// <c>ProviderMetricsHandler</c> (<c>bridgebeats.provider.requests.total</c>):
/// the handler records one observation per HTTP call, while <c>RecordBatchSize</c>
/// records how many IDs were included in each call, making the batching efficiency
/// visible without additional query-time joins.
/// </para>
/// </remarks>
public static class SpotifyBatchMetrics {

    /// <summary>
    /// The meter name for Spotify batch metrics.
    /// </summary>
    public const string MeterName = "BridgeBeats.Spotify.Batch";

    /// <summary>
    /// The meter for Spotify batch metrics.
    /// </summary>
    public static readonly Meter Meter = new( MeterName, "1.0.0" );

    /// <summary>
    /// Tag key for the lookup type (tracks / albums).
    /// </summary>
    public const string LookupTypeTag = "lookup_type";

    #region Histograms

    /// <summary>
    /// Number of IDs included in each bulk Spotify API call.
    /// </summary>
    /// <remarks>
    /// Tags: lookup_type (tracks | albums).
    /// A value of 1 means the linger timer fired before enough messages accumulated.
    /// A value at the configured maximum (50 for tracks, 20 for albums) means the size
    /// threshold triggered the flush. Values between indicate partial fills — normal
    /// in lower-volume deployments.
    /// </remarks>
    public static readonly Histogram<int> BatchSize = Meter.CreateHistogram<int>(
        "bridgebeats.spotify.batch.size",
        unit: "{ids}",
        description: "Number of Spotify IDs included in a single bulk API call"
    );

    #endregion

    #region Helper Methods

    /// <summary>
    /// Records the size of a Spotify bulk API call.
    /// </summary>
    /// <param name="lookupType">The type of lookup: "tracks" or "albums".</param>
    /// <param name="count">The number of IDs included in the call.</param>
    public static void RecordBatchSize( string lookupType, int count ) {
        BatchSize.Record(
            count,
            new KeyValuePair<string, object?>( LookupTypeTag, lookupType )
        );
    }

    #endregion
}
