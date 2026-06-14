using System.Diagnostics.Metrics;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>
/// OpenTelemetry-compatible metric instruments for the BridgeBeats queue system and rate limiter.
/// </summary>
/// <remarks>
/// All instruments are created on a single <see cref="System.Diagnostics.Metrics.Meter"/>
/// named <see cref="MeterName"/> (instruments are prefixed with <c>bridgebeats.queue.</c> and
/// <c>bridgebeats.ratelimit.</c>); register with <c>.AddMeter("BridgeBeats.Queue")</c>. Counters
/// and histograms are recorded through the helper methods in this class; the observable gauges poll
/// live Redis stream lengths each time the metrics system collects, and are registered once via
/// <see cref="RegisterQueueDepthGauges(StackExchange.Redis.IConnectionMultiplexer)"/>.
/// </remarks>
public static class QueueMetrics {

    /// <summary>
    /// Name of the meter that owns every instrument in this class.
    /// Literal value: <c>"BridgeBeats.Queue"</c>; subscribe to this name to collect the metrics.
    /// </summary>
    public const string MeterName = "BridgeBeats.Queue";

    /// <summary>
    /// The shared meter (version 1.0.0) on which all queue instruments are created.
    /// </summary>
    public static readonly Meter Meter = new( MeterName, "1.0.0" );

    #region Counters

    /// <summary>
    /// Counts requests enqueued onto any priority lane, tagged by provider and priority.
    /// </summary>
    public static readonly Counter<long> EnqueuedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.enqueued.total",
        unit: "{requests}",
        description: "Total number of requests enqueued"
    );

    /// <summary>
    /// Counts requests dequeued for processing, tagged by provider and the priority lane
    /// the message was pulled from.
    /// </summary>
    public static readonly Counter<long> DequeuedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.dequeued.total",
        unit: "{requests}",
        description: "Total number of requests dequeued for processing"
    );

    /// <summary>
    /// Counts requests successfully processed and acknowledged (XACK + XDEL), tagged by provider.
    /// </summary>
    public static readonly Counter<long> AcknowledgedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.acknowledged.total",
        unit: "{requests}",
        description: "Total number of requests successfully processed"
    );

    /// <summary>
    /// Counts requests returned to the queue for retry, tagged by provider.
    /// </summary>
    public static readonly Counter<long> RequeuedTotal = Meter.CreateCounter<long>(
        "bridgebeats.queue.requeued.total",
        unit: "{requests}",
        description: "Total number of requests returned to queue for retry"
    );

    /// <summary>
    /// Counts rate-limit events encountered, tagged by provider and endpoint.
    /// </summary>
    public static readonly Counter<long> RateLimitEventsTotal = Meter.CreateCounter<long>(
        "bridgebeats.ratelimit.events.total",
        unit: "{events}",
        description: "Total number of rate limit events encountered"
    );

    /// <summary>
    /// Counts duplicate requests prevented by the single-flight deduplicator (untagged).
    /// </summary>
    public static readonly Counter<long> DeduplicatedTotal = Meter.CreateCounter<long>(
        "bridgebeats.dedup.prevented.total",
        unit: "{requests}",
        description: "Total number of duplicate requests prevented"
    );

    /// <summary>
    /// Counts interactive-origin lookups that were deferred to the background retry lane after a
    /// provider rate limit, tagged by provider and endpoint. This is the signal that interactive
    /// callers are being pushed onto slower processing because an endpoint is throttled.
    /// </summary>
    public static readonly Counter<long> InteractiveDeferredTotal = Meter.CreateCounter<long>(
        "bridgebeats.ratelimit.interactive_deferred.total",
        unit: "{requests}",
        description: "Total number of interactive-origin lookups deferred to background due to rate limiting"
    );

    #endregion

    #region Histograms

    /// <summary>
    /// Records, in seconds, how long it took to process a queued request, tagged by provider,
    /// lookup type, and status.
    /// </summary>
    public static readonly Histogram<double> ProcessingDuration = Meter.CreateHistogram<double>(
        "bridgebeats.queue.processing.duration",
        unit: "s",
        description: "Time taken to process a queued request"
    );

    /// <summary>
    /// Records, in seconds, the Retry-After window of each rate-limit event, tagged by
    /// provider and endpoint.
    /// </summary>
    public static readonly Histogram<double> RateLimitDuration = Meter.CreateHistogram<double>(
        "bridgebeats.ratelimit.duration",
        unit: "s",
        description: "Duration of rate limit Retry-After periods"
    );

    #endregion

    #region Observable Gauges

    /// <summary>Guards against registering the observable gauges more than once.</summary>
    private static bool s_gaugesRegistered;

    /// <summary>Serializes the one-time gauge registration.</summary>
    private static readonly Lock s_gaugesLock = new( );

    /// <summary>
    /// Registers observable gauges for queue depth metrics, which poll live Redis stream lengths on
    /// each metrics collection.
    /// </summary>
    /// <param name="redis">The Redis connection used to read stream lengths.</param>
    /// <remarks>
    /// Call this once during application startup after Redis is connected; the gauges automatically
    /// poll queue depths on each metrics collection. Idempotent and thread-safe: registration
    /// happens at most once per process even if this is called from several startup paths. Three
    /// gauges are created: <c>bridgebeats.queue.depth</c> (one measurement per provider and priority
    /// lane), <c>bridgebeats.queue.spotify.bulk.track.depth</c>, and
    /// <c>bridgebeats.queue.spotify.bulk.album.depth</c> (the two dedicated Spotify bulk streams).
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redis"/> is null.</exception>
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
    /// Yields one queue-depth measurement per provider and priority lane (interactive,
    /// background, bulk) by reading the length of each <c>queue:{provider}:{priority}</c> stream.
    /// A read failure for any single stream yields a depth of zero rather than throwing.
    /// </summary>
    /// <param name="redis">The Redis connection used to read stream lengths.</param>
    /// <returns>One measurement per provider/priority combination, tagged with provider and priority.</returns>
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
    /// Yields a single depth measurement for one Spotify type-specific bulk stream. A read failure
    /// yields a depth of zero rather than throwing.
    /// </summary>
    /// <param name="redis">The Redis connection used to read the stream length.</param>
    /// <param name="stream">The bulk stream key to measure (track-id or album-id stream).</param>
    /// <returns>One measurement tagged with provider "spotify" and the stream key.</returns>
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
    /// Records a queue enqueue event: increments <see cref="EnqueuedTotal"/> with standard tags.
    /// </summary>
    /// <param name="provider">The provider the request was enqueued for.</param>
    /// <param name="priority">The priority lane the request was enqueued to.</param>
    public static void RecordEnqueue( SupportedProviders provider, QueuePriority priority ) {
        EnqueuedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, priority.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a queue dequeue event: increments <see cref="DequeuedTotal"/> with standard tags.
    /// </summary>
    /// <param name="provider">The provider the request was dequeued from.</param>
    /// <param name="priority">The priority lane the request was dequeued from.</param>
    public static void RecordDequeue( SupportedProviders provider, QueuePriority priority ) {
        DequeuedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, priority.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a message acknowledgment event: increments <see cref="AcknowledgedTotal"/> with standard tags.
    /// </summary>
    /// <param name="provider">The provider the message was acknowledged for.</param>
    public static void RecordAcknowledge( SupportedProviders provider ) {
        AcknowledgedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a message requeue event: increments <see cref="RequeuedTotal"/> with standard tags.
    /// </summary>
    /// <param name="provider">The provider the message was requeued for.</param>
    public static void RecordRequeue( SupportedProviders provider ) {
        RequeuedTotal.Add(
            1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider.ToString( ).ToLowerInvariant( ) )
        );
    }

    /// <summary>
    /// Records a rate-limit event: increments <see cref="RateLimitEventsTotal"/> and records the
    /// Retry-After window on <see cref="RateLimitDuration"/>, both with standard tags.
    /// </summary>
    /// <param name="provider">The provider that encountered the rate limit.</param>
    /// <param name="endpoint">The endpoint that was rate limited.</param>
    /// <param name="retryAfterSeconds">The Retry-After duration, in seconds.</param>
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
    /// Records a deduplication event: increments <see cref="DeduplicatedTotal"/>.
    /// </summary>
    public static void RecordDeduplicated( ) {
        DeduplicatedTotal.Add( 1 );
    }

    /// <summary>
    /// Records the processing duration for a queued request on <see cref="ProcessingDuration"/>.
    /// </summary>
    /// <param name="provider">The provider that processed the request.</param>
    /// <param name="lookupType">The type of lookup that was performed.</param>
    /// <param name="status">The processing status (success, failure, rate_limited).</param>
    /// <param name="durationSeconds">The processing duration, in seconds.</param>
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
    /// and requeued at Background priority — by incrementing <see cref="InteractiveDeferredTotal"/>.
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
