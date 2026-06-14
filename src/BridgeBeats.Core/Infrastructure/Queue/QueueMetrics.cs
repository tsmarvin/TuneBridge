using System.Diagnostics.Metrics;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// OpenTelemetry metrics for the BridgeBeats queue system.
/// </summary>
/// <remarks>
/// All metrics prefixed with <c>bridgebeats.queue.</c>; register with <c>.AddMeter("BridgeBeats.Queue")</c>.
/// </remarks>
public static class QueueMetrics {

    /// <summary>
    /// The meter name for queue metrics.
    /// </summary>
    public const string MeterName = "BridgeBeats.Queue";

    /// <summary>
    /// The meter for queue metrics.
    /// </summary>
    public static readonly Meter Meter = new( MeterName, "1.0.0" );

    #region Counters

    /// <summary>
    /// Total number of requests enqueued to provider queues.
    /// </summary>
    /// <remarks>
    /// Tags: provider, priority
    /// </remarks>
    public static readonly Counter<long> EnqueuedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.enqueued.total",
        unit: "{requests}",
        description: "Total number of requests enqueued"
    );

    /// <summary>
    /// Total number of requests dequeued for processing.
    /// </summary>
    /// <remarks>
    /// Tags: provider, priority
    /// </remarks>
    public static readonly Counter<long> DequeuedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.dequeued.total",
        unit: "{requests}",
        description: "Total number of requests dequeued for processing"
    );

    /// <summary>
    /// Total number of requests successfully processed and acknowledged.
    /// </summary>
    /// <remarks>
    /// Tags: provider, priority
    /// </remarks>
    public static readonly Counter<long> AcknowledgedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.acknowledged.total",
        unit: "{requests}",
        description: "Total number of requests successfully processed"
    );

    /// <summary>
    /// Total number of requests returned to the queue for retry.
    /// </summary>
    /// <remarks>
    /// Tags: provider, priority
    /// </remarks>
    public static readonly Counter<long> RequeuedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.requeued.total",
        unit: "{requests}",
        description: "Total number of requests returned to queue for retry"
    );

    /// <summary>
    /// Total number of rate limit events encountered.
    /// </summary>
    /// <remarks>
    /// Tags: provider, endpoint
    /// </remarks>
    public static readonly Counter<long> RateLimitEventsTotal = Meter.CreateCounter<long>(
        "bridgebeats.ratelimit.events.total",
        unit: "{events}",
        description: "Total number of rate limit events encountered"
    );

    /// <summary>
    /// Total number of duplicate requests prevented by deduplication.
    /// </summary>
    public static readonly Counter<long> DeduplicatedTotal = Meter.CreateCounter<long>(
        "bridgebeats.dedup.prevented.total",
        unit: "{requests}",
        description: "Total number of duplicate requests prevented"
    );

    /// <summary>
    /// Total number of interactive-origin lookups deferred to the background retry lane
    /// due to a provider rate limit.
    /// </summary>
    /// <remarks>
    /// Tags: provider, endpoint
    /// </remarks>
    public static readonly Counter<long> InteractiveDeferredTotal = Meter.CreateCounter<long>(
        "bridgebeats.ratelimit.interactive_deferred.total",
        unit: "{requests}",
        description: "Total number of interactive-origin lookups deferred to background due to rate limiting"
    );

    #endregion

    #region Histograms

    /// <summary>
    /// Time taken to process a queued request.
    /// </summary>
    /// <remarks>
    /// Tags: provider, lookup_type, status
    /// </remarks>
    public static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "bridgebeats.queue.processing.duration",
        unit: "s",
        description: "Time taken to process a queued request"
    );

    /// <summary>
    /// Duration of rate limit Retry-After periods.
    /// </summary>
    /// <remarks>
    /// Tags: provider, endpoint
    /// </remarks>
    public static readonly Histogram<double> RateLimitDuration = Meter.CreateHistogram<double>(
        "bridgebeats.ratelimit.duration",
        unit: "s",
        description: "Duration of rate limit Retry-After periods"
    );

    #endregion

    #region Observable Gauges

    private static bool s_gaugesRegistered;
    private static readonly Lock s_gaugesLock = new( );

    /// <summary>
    /// Registers observable gauges for queue depth metrics.
    /// </summary>
    /// <remarks>
    /// Call this once during application startup after Redis is connected.
    /// The gauges will automatically poll queue depths on each metrics collection.
    /// </remarks>
    /// <param name="redis">The Redis connection multiplexer.</param>
    public static void RegisterQueueDepthGauges( IConnectionMultiplexer redis ) {
        ArgumentNullException.ThrowIfNull( redis );

        lock (s_gaugesLock) {
            if (s_gaugesRegistered) {
                return;
            }

            // Create a single observable gauge that returns measurements for all provider/priority combinations
            _ = Meter.CreateObservableGauge(
                "bridgebeats.queue.depth",
                ( ) => GetQueueDepthMeasurements( redis ),
                unit: "{requests}",
                description: "Current number of requests in queue"
            );

            // Spotify type-specific bulk streams sit outside the generic priority layout and need separate gauges.
            _ = Meter.CreateObservableGauge(
                "bridgebeats.queue.spotify.bulk.track.depth",
                ( ) => GetSpotifyBulkStreamDepth( redis, SpotifyConstants.BulkTrackIdStream ),
                unit: "{requests}",
                description: "Current depth of the Spotify bulk track-id stream (queue:spotify:bulk:track-id)"
            );

            _ = Meter.CreateObservableGauge(
                "bridgebeats.queue.spotify.bulk.album.depth",
                ( ) => GetSpotifyBulkStreamDepth( redis, SpotifyConstants.BulkAlbumIdStream ),
                unit: "{requests}",
                description: "Current depth of the Spotify bulk album-id stream (queue:spotify:bulk:album-id)"
            );

            s_gaugesRegistered = true;
        }
    }

    /// <summary>
    /// Gets queue depth measurements for all provider/priority combinations.
    /// </summary>
    private static IEnumerable<Measurement<long>> GetQueueDepthMeasurements( IConnectionMultiplexer redis ) {
        IDatabase db = redis.GetDatabase( );
        string[] priorities = ["interactive", "background", "bulk"];

        foreach (SupportedProviders provider in Enum.GetValues<SupportedProviders>( )) {
            string providerName = provider.ToString( ).ToLowerInvariant( );

            foreach (string priority in priorities) {
                string streamKey = $"queue:{providerName}:{priority}";

                long length;
                try {
                    length = db.StreamLength( streamKey );
                } catch {
                    // Stream may not exist yet
                    length = 0;
                }

                yield return new Measurement<long>(
                    length,
                    new KeyValuePair<string, object?>( QueueMetricTags.Provider, providerName ),
                    new KeyValuePair<string, object?>( QueueMetricTags.Priority, priority )
                );
            }
        }
    }

    /// <summary>
    /// Returns a single depth measurement for a Spotify type-specific bulk stream.
    /// </summary>
    private static IEnumerable<Measurement<long>> GetSpotifyBulkStreamDepth( IConnectionMultiplexer redis, string stream ) {
        IDatabase db = redis.GetDatabase( );
        long length;
        try {
            length = db.StreamLength( stream );
        } catch {
            length = 0;
        }

        yield return new Measurement<long>(
            length,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
            new KeyValuePair<string, object?>( "stream", stream )
        );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Records a queue enqueue event with standard tags.
    /// </summary>
    /// <param name="provider">The provider the request was enqueued for.</param>
    /// <param name="priority">The priority level of the request.</param>
    public static void RecordEnqueue( SupportedProviders provider, QueuePriority priority ) {
        EnqueuedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, priority.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a queue dequeue event with standard tags.
    /// </summary>
    /// <param name="provider">The provider the request was dequeued from.</param>
    /// <param name="priority">The priority level of the request.</param>
    public static void RecordDequeue( SupportedProviders provider, QueuePriority priority ) {
        DequeuedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, priority.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a message acknowledgment event with standard tags.
    /// </summary>
    /// <param name="provider">The provider the message was acknowledged for.</param>
    public static void RecordAcknowledge( SupportedProviders provider ) {
        AcknowledgedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a message requeue event with standard tags.
    /// </summary>
    /// <param name="provider">The provider the message was requeued for.</param>
    public static void RecordRequeue( SupportedProviders provider ) {
        RequeuedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a rate limit event with standard tags.
    /// </summary>
    /// <param name="provider">The provider that encountered the rate limit.</param>
    /// <param name="endpoint">The endpoint that was rate limited.</param>
    /// <param name="retryAfterSeconds">The Retry-After duration in seconds.</param>
    public static void RecordRateLimitEvent( SupportedProviders provider, string endpoint, double retryAfterSeconds ) {
        string providerName = provider.ToString( ).ToLowerInvariant( );

        RateLimitEventsTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, providerName ),
            new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, endpoint )
        );

        RateLimitDuration.Record(
            retryAfterSeconds,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, providerName ),
            new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, endpoint )
        );
    }

    /// <summary>
    /// Records a deduplication event.
    /// </summary>
    public static void RecordDeduplicated( ) {
        DeduplicatedTotal.Add( 1 );
    }

    /// <summary>
    /// Records the processing duration for a queued request.
    /// </summary>
    /// <param name="provider">The provider that processed the request.</param>
    /// <param name="lookupType">The type of lookup that was performed.</param>
    /// <param name="status">The processing status (success, failure, rate_limited).</param>
    /// <param name="durationSeconds">The processing duration in seconds.</param>
    public static void RecordProcessingDuration(
        SupportedProviders provider,
        LookupRequestType lookupType,
        string status,
        double durationSeconds
    ) {
        ProcessingDuration.Record(
            durationSeconds,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) ),
            new KeyValuePair<string, object?>( QueueMetricTags.LookupType, lookupType.ToString( ) ),
            new KeyValuePair<string, object?>( QueueMetricTags.Status, status )
        );
    }

    /// <summary>
    /// Records an interactive-origin deferral event — an interactive lookup that was rate-limited
    /// and requeued at Background priority.
    /// </summary>
    /// <param name="provider">The provider that encountered the rate limit.</param>
    /// <param name="endpoint">The endpoint that was rate limited.</param>
    public static void RecordInteractiveDeferral( SupportedProviders provider, string endpoint ) {
        string providerName = provider.ToString( ).ToLowerInvariant( );

        InteractiveDeferredTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, providerName ),
            new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, endpoint )
        );
    }

    #endregion
}
