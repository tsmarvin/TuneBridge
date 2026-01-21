using System.Diagnostics.Metrics;
using BridgeBeats.Infrastructure.Queue;
using StackExchange.Redis;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="QueueMetrics"/> observable gauge functionality.
/// Requires Docker to be running on the host machine for Redis Testcontainer.
/// </summary>
[TestClass]
[TestCategory( "Integration" )]
[TestCategory( "Docker" )]
[DoNotParallelize]
public class QueueMetricsIntegrationTests {

    private static IConnectionMultiplexer? s_redis;

    /// <summary>
    /// Initializes shared Redis connection for all tests in this class.
    /// </summary>
    /// <param name="context">The test context provided by MSTest.</param>
    [ClassInitialize]
    [Obsolete]
    public static async Task ClassInitialize( TestContext context ) {
        SharedTestInfrastructure.RequireRedis( );
        s_redis = await ConnectionMultiplexer.ConnectAsync( SharedTestInfrastructure.RedisConnectionString );
    }

    /// <summary>
    /// Cleans up the Redis connection after all tests in this class have completed.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup( ) {
        if (s_redis is not null) {
            await s_redis.CloseAsync( );
            s_redis.Dispose( );
        }
    }

    /// <summary>
    /// Clears queue-related keys before each test to ensure test isolation.
    /// </summary>
    [TestInitialize]
    public async Task TestInitialize( ) {
        // Clear queue-related keys before each test
        IDatabase db = s_redis!.GetDatabase( );
        IServer server = s_redis.GetServer( s_redis.GetEndPoints( )[0] );
        await foreach (RedisKey key in server.KeysAsync( pattern: "queue:*" )) {
            _ = await db.KeyDeleteAsync( key );
        }
    }

    #region Observable Gauge Tests

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RegisterQueueDepthGauges"/> throws <see cref="ArgumentNullException"/> when passed a null Redis connection.
    /// </summary>
    [TestMethod]
    public void RegisterQueueDepthGauges_ThrowsOnNullRedis( ) {
        // Act & Assert
        _ = Assert.ThrowsExactly<ArgumentNullException>( ( ) =>
            QueueMetrics.RegisterQueueDepthGauges( null! )
        );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RegisterQueueDepthGauges"/> does not throw when passed a valid Redis connection.
    /// </summary>
    [TestMethod]
    public void RegisterQueueDepthGauges_DoesNotThrow( ) {
        // Act - Should not throw
        try {
            QueueMetrics.RegisterQueueDepthGauges( s_redis! );
        } catch (Exception ex) {
            Assert.Fail( $"RegisterQueueDepthGauges should not throw: {ex.Message}" );
        }

        // Assert - No exception means success
        Assert.IsNotNull( s_redis );
    }

    /// <summary>
    /// Verifies that the queue depth gauge reports zero for empty or non-existent Redis streams.
    /// </summary>
    [TestMethod]
    public async Task QueueDepthGauge_ReportsZeroForEmptyStreams( ) {
        // Arrange
        QueueMetrics.RegisterQueueDepthGauges( s_redis! );

        List<Measurement<long>> measurements = [];

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.depth") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            string? provider = null;
            string? priority = null;

            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    provider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    priority = tag.Value?.ToString( );
                }
            }

            measurements.Add( new Measurement<long>( measurement,
                new KeyValuePair<string, object?>( QueueMetricTags.Provider, provider ),
                new KeyValuePair<string, object?>( QueueMetricTags.Priority, priority )
            ) );
        } );

        listener.Start( );

        // Act - Trigger observable gauge collection
        listener.RecordObservableInstruments( );

        // Assert - All measurements should be 0 for empty/non-existent streams
        Assert.IsNotEmpty( measurements, "Should have recorded measurements" );
        foreach (Measurement<long> measurement in measurements) {
            Assert.AreEqual( 0, measurement.Value, $"Empty stream should report 0 depth" );
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Verifies that the queue depth gauge reports the correct depth for a populated Redis stream.
    /// </summary>
    [TestMethod]
    public async Task QueueDepthGauge_ReportsCorrectDepthForPopulatedStream( ) {
        // Arrange
        QueueMetrics.RegisterQueueDepthGauges( s_redis! );
        IDatabase db = s_redis!.GetDatabase( );

        // Add messages to a specific stream
        string streamKey = "queue:spotify:interactive";
        for (int i = 0; i < 5; i++) {
            _ = await db.StreamAddAsync( streamKey, "data", $"message-{i}" );
        }

        long spotifyInteractiveDepth = 0;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.depth") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            string? provider = null;
            string? priority = null;

            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    provider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    priority = tag.Value?.ToString( );
                }
            }

            if (provider == "spotify" && priority == "interactive") {
                spotifyInteractiveDepth = measurement;
            }
        } );

        listener.Start( );

        // Act - Trigger observable gauge collection
        listener.RecordObservableInstruments( );

        // Assert
        Assert.AreEqual( 5, spotifyInteractiveDepth, "Stream with 5 messages should report depth of 5" );
    }

    /// <summary>
    /// Verifies that the queue depth gauge correctly reports depths for multiple providers and priorities.
    /// </summary>
    [TestMethod]
    public async Task QueueDepthGauge_ReportsMultipleProviderDepths( ) {
        // Arrange
        QueueMetrics.RegisterQueueDepthGauges( s_redis! );
        IDatabase db = s_redis!.GetDatabase( );

        // Add different numbers of messages to different streams
        _ = await db.StreamAddAsync( "queue:spotify:interactive", "data", "msg1" );
        _ = await db.StreamAddAsync( "queue:spotify:interactive", "data", "msg2" );
        _ = await db.StreamAddAsync( "queue:spotify:interactive", "data", "msg3" );

        _ = await db.StreamAddAsync( "queue:applemusic:background", "data", "msg1" );
        _ = await db.StreamAddAsync( "queue:applemusic:background", "data", "msg2" );

        _ = await db.StreamAddAsync( "queue:tidal:bulk", "data", "msg1" );

        Dictionary<string, long> depths = [];

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.depth") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            string? provider = null;
            string? priority = null;

            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    provider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    priority = tag.Value?.ToString( );
                }
            }

            if (provider is not null && priority is not null) {
                depths[$"{provider}:{priority}"] = measurement;
            }
        } );

        listener.Start( );

        // Act - Trigger observable gauge collection
        listener.RecordObservableInstruments( );

        // Assert
        Assert.AreEqual( 3, depths.GetValueOrDefault( "spotify:interactive" ), "Spotify interactive should have 3 messages" );
        Assert.AreEqual( 2, depths.GetValueOrDefault( "applemusic:background" ), "Apple Music background should have 2 messages" );
        Assert.AreEqual( 1, depths.GetValueOrDefault( "tidal:bulk" ), "Tidal bulk should have 1 message" );
    }

    /// <summary>
    /// Verifies that the queue depth gauge includes measurements for all provider and priority combinations.
    /// </summary>
    [TestMethod]
    public void QueueDepthGauge_IncludesAllProviderPriorityCombinations( ) {
        // Arrange
        QueueMetrics.RegisterQueueDepthGauges( s_redis! );

        HashSet<string> recordedCombinations = [];

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.depth") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            string? provider = null;
            string? priority = null;

            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    provider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    priority = tag.Value?.ToString( );
                }
            }

            if (provider is not null && priority is not null) {
                _ = recordedCombinations.Add( $"{provider}:{priority}" );
            }
        } );

        listener.Start( );

        // Act - Trigger observable gauge collection
        listener.RecordObservableInstruments( );

        // Assert - Should have all provider/priority combinations (3 providers x 3 priorities = 9)
        string[] expectedProviders = ["spotify", "applemusic", "tidal"];
        string[] expectedPriorities = ["interactive", "background", "bulk"];

        foreach (string provider in expectedProviders) {
            foreach (string priority in expectedPriorities) {
                string combination = $"{provider}:{priority}";
                Assert.Contains( combination, recordedCombinations,
                    $"Expected combination '{combination}' not found in measurements" );
            }
        }

        Assert.HasCount( 9, recordedCombinations, "Should have exactly 9 provider/priority combinations" );
    }

    #endregion
}
