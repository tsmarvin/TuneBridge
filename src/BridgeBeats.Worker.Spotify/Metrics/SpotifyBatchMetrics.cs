using System.Diagnostics.Metrics;

namespace BridgeBeats.Worker.Spotify.Metrics;

/// <summary>
/// OpenTelemetry-compatible metrics for the Spotify bulk-batch lookup path.
/// </summary>
/// <remarks>
/// This worker-local meter records how many ids are packed into each bulk Spotify API call,
/// which is the main observability signal for whether batching is effective (large batches)
/// or degenerating into near-single-id calls (small batches). Register with
/// <c>.AddMeter("BridgeBeats.Spotify.Batch")</c>. Complements the per-HTTP-call metrics from
/// <c>ProviderMetricsHandler</c> by recording batch sizes per call.
/// </remarks>
public static class SpotifyBatchMetrics {

    /// <summary>
    /// Name of the meter under which the batch instruments are published (<c>BridgeBeats.Spotify.Batch</c>).
    /// </summary>
    public const string MeterName = "BridgeBeats.Spotify.Batch";

    /// <summary>
    /// The shared <see cref="System.Diagnostics.Metrics.Meter"/> that owns the batch instruments.
    /// </summary>
    public static readonly Meter Meter = new( MeterName, "1.0.0" );

    /// <summary>
    /// Tag name distinguishing the kind of lookup a measurement belongs to (for example
    /// <c>tracks</c> or <c>albums</c>).
    /// </summary>
    public const string LookupTypeTag = "lookup_type";

    #region Histograms

    /// <summary>
    /// Histogram of the number of Spotify ids included in a single bulk API call, tagged by
    /// <see cref="LookupTypeTag"/>.
    /// </summary>
    /// <remarks>
    /// Recorded once per flushed batch. A value of 1 means the linger timer fired before enough
    /// messages accumulated. A value at the configured maximum (50 for tracks, 20 for albums) means
    /// the size threshold triggered the flush. Values between indicate partial fills — normal in
    /// lower-volume deployments.
    /// </remarks>
    public static readonly Histogram<int> BatchSize = Meter.CreateHistogram<int>(
        "bridgebeats.spotify.batch.size",
        unit: "{ids}",
        description: "Number of Spotify IDs included in a single bulk API call"
    );

    #endregion

    #region Helper Methods

    /// <summary>
    /// Records a single batch-size measurement to <see cref="BatchSize"/>.
    /// </summary>
    /// <param name="lookupType">The kind of lookup (for example <c>tracks</c> or <c>albums</c>), emitted as the <see cref="LookupTypeTag"/> tag value.</param>
    /// <param name="count">The number of ids included in the bulk call.</param>
    public static void RecordBatchSize( string lookupType, int count ) {
        BatchSize.Record(
            count,
            new KeyValuePair<string, object?>( LookupTypeTag, lookupType )
        );
    }

    #endregion
}
