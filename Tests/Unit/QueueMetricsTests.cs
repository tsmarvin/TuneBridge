using System.Diagnostics.Metrics;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Infrastructure.Queue;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QueueMetrics"/> and <see cref="QueueMetricTags"/>.
/// Validates metric definitions, tag constants, and instrumentation correctness.
/// </summary>
[TestClass]
public class QueueMetricsTests {

    #region Meter Tests

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.Meter"/> has the expected name "BridgeBeats.Queue".
    /// </summary>
    [TestMethod]
    public void Meter_HasCorrectName( ) {
        // Arrange
        const string ExpectedName = "BridgeBeats.Queue";

        // Assert
        Assert.AreEqual( ExpectedName, QueueMetrics.Meter.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.Meter"/> has the expected version "1.0.0".
    /// </summary>
    [TestMethod]
    public void Meter_HasCorrectVersion( ) {
        // Assert
        Assert.AreEqual( "1.0.0", QueueMetrics.Meter.Version );
    }

    #endregion

    #region Counter Tests

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.EnqueuedTotal"/> counter is properly initialized.
    /// </summary>
    [TestMethod]
    public void EnqueuedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.EnqueuedTotal );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.EnqueuedTotal"/> counter has the expected metric name.
    /// </summary>
    [TestMethod]
    public void EnqueuedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.enqueued.total", QueueMetrics.EnqueuedTotal.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.DequeuedTotal"/> counter is properly initialized.
    /// </summary>
    [TestMethod]
    public void DequeuedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.DequeuedTotal );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.DequeuedTotal"/> counter has the expected metric name.
    /// </summary>
    [TestMethod]
    public void DequeuedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.dequeued.total", QueueMetrics.DequeuedTotal.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.AcknowledgedTotal"/> counter is properly initialized.
    /// </summary>
    [TestMethod]
    public void AcknowledgedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.AcknowledgedTotal );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.AcknowledgedTotal"/> counter has the expected metric name.
    /// </summary>
    [TestMethod]
    public void AcknowledgedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.acknowledged.total", QueueMetrics.AcknowledgedTotal.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RequeuedTotal"/> counter is properly initialized.
    /// </summary>
    [TestMethod]
    public void RequeuedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.RequeuedTotal );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RequeuedTotal"/> counter has the expected metric name.
    /// </summary>
    [TestMethod]
    public void RequeuedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.requeued.total", QueueMetrics.RequeuedTotal.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RateLimitEventsTotal"/> counter is properly initialized.
    /// </summary>
    [TestMethod]
    public void RateLimitEventsTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.RateLimitEventsTotal );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RateLimitEventsTotal"/> counter has the expected metric name.
    /// </summary>
    [TestMethod]
    public void RateLimitEventsTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.ratelimit.events.total", QueueMetrics.RateLimitEventsTotal.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.DeduplicatedTotal"/> counter is properly initialized.
    /// </summary>
    [TestMethod]
    public void DeduplicatedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.DeduplicatedTotal );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.DeduplicatedTotal"/> counter has the expected metric name.
    /// </summary>
    [TestMethod]
    public void DeduplicatedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.dedup.prevented.total", QueueMetrics.DeduplicatedTotal.Name );
    }

    #endregion

    #region Histogram Tests

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.ProcessingDuration"/> histogram is properly initialized.
    /// </summary>
    [TestMethod]
    public void ProcessingDuration_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.ProcessingDuration );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.ProcessingDuration"/> histogram has the expected metric name.
    /// </summary>
    [TestMethod]
    public void ProcessingDuration_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.processing.duration", QueueMetrics.ProcessingDuration.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.ProcessingDuration"/> histogram uses seconds as the unit.
    /// </summary>
    [TestMethod]
    public void ProcessingDuration_HasCorrectUnit( ) {
        // Assert
        Assert.AreEqual( "s", QueueMetrics.ProcessingDuration.Unit );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RateLimitDuration"/> histogram is properly initialized.
    /// </summary>
    [TestMethod]
    public void RateLimitDuration_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.RateLimitDuration );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RateLimitDuration"/> histogram has the expected metric name.
    /// </summary>
    [TestMethod]
    public void RateLimitDuration_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.ratelimit.duration", QueueMetrics.RateLimitDuration.Name );
    }

    /// <summary>
    /// Verifies that the <see cref="QueueMetrics.RateLimitDuration"/> histogram uses seconds as the unit.
    /// </summary>
    [TestMethod]
    public void RateLimitDuration_HasCorrectUnit( ) {
        // Assert
        Assert.AreEqual( "s", QueueMetrics.RateLimitDuration.Unit );
    }

    #endregion

    #region Counter Recording Tests

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.EnqueuedTotal"/> can record values with provider and priority tags without throwing.
    /// </summary>
    [TestMethod]
    public void EnqueuedTotal_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        QueueMetrics.EnqueuedTotal.Add( 1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, "interactive" )
        );

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.EnqueuedTotal );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.DequeuedTotal"/> can record values with provider and priority tags without throwing.
    /// </summary>
    [TestMethod]
    public void DequeuedTotal_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        QueueMetrics.DequeuedTotal.Add( 1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "applemusic" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, "background" )
        );

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.DequeuedTotal );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RateLimitEventsTotal"/> can record values with provider and endpoint tags without throwing.
    /// </summary>
    [TestMethod]
    public void RateLimitEventsTotal_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        QueueMetrics.RateLimitEventsTotal.Add( 1,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "tidal" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, "/v1/tracks" )
        );

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.RateLimitEventsTotal );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.DeduplicatedTotal"/> can record values without tags.
    /// </summary>
    [TestMethod]
    public void DeduplicatedTotal_CanRecordWithoutTags( ) {
        // Arrange & Act - Should not throw
        QueueMetrics.DeduplicatedTotal.Add( 1 );

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.DeduplicatedTotal );
    }

    #endregion

    #region Histogram Recording Tests

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.ProcessingDuration"/> can record values with provider, lookup type, and status tags without throwing.
    /// </summary>
    [TestMethod]
    public void ProcessingDuration_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        QueueMetrics.ProcessingDuration.Record( 1.5,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
            new KeyValuePair<string, object?>( QueueMetricTags.LookupType, "IsrcLookup" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Status, "success" )
        );

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.ProcessingDuration );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RateLimitDuration"/> can record values with provider and endpoint tags without throwing.
    /// </summary>
    [TestMethod]
    public void RateLimitDuration_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        QueueMetrics.RateLimitDuration.Record( 60.0,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, "/v1/albums" )
        );

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.RateLimitDuration );
    }

    #endregion

    #region MeterListener Verification Tests

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.EnqueuedTotal"/> correctly records values and tags using a MeterListener.
    /// </summary>
    [TestMethod]
    public void EnqueuedTotal_RecordsCorrectValueWithListener( ) {
        // Arrange
        long recordedValue = 0;
        string? recordedProvider = null;
        string? recordedPriority = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.enqueued.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    recordedPriority = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.EnqueuedTotal.Add( 5,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Priority, "interactive" )
        );

        // Assert
        Assert.AreEqual( 5, recordedValue );
        Assert.AreEqual( "spotify", recordedProvider );
        Assert.AreEqual( "interactive", recordedPriority );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.ProcessingDuration"/> correctly records duration values and status tags using a MeterListener.
    /// </summary>
    [TestMethod]
    public void ProcessingDuration_RecordsCorrectValueWithListener( ) {
        // Arrange
        double recordedValue = 0;
        string? recordedStatus = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.processing.duration") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<double>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Status) {
                    recordedStatus = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.ProcessingDuration.Record( 2.5,
            new KeyValuePair<string, object?>( QueueMetricTags.Provider, "applemusic" ),
            new KeyValuePair<string, object?>( QueueMetricTags.LookupType, "UpcLookup" ),
            new KeyValuePair<string, object?>( QueueMetricTags.Status, "success" )
        );

        // Assert
        Assert.AreEqual( 2.5, recordedValue, 0.001 );
        Assert.AreEqual( "success", recordedStatus );
    }

    #endregion

    #region Counter Unit Tests

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.EnqueuedTotal"/> uses the correct unit "{requests}".
    /// </summary>
    [TestMethod]
    public void EnqueuedTotal_HasCorrectUnit( ) {
        Assert.AreEqual( "{requests}", QueueMetrics.EnqueuedTotal.Unit );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.DequeuedTotal"/> uses the correct unit "{requests}".
    /// </summary>
    [TestMethod]
    public void DequeuedTotal_HasCorrectUnit( ) {
        Assert.AreEqual( "{requests}", QueueMetrics.DequeuedTotal.Unit );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.AcknowledgedTotal"/> uses the correct unit "{requests}".
    /// </summary>
    [TestMethod]
    public void AcknowledgedTotal_HasCorrectUnit( ) {
        Assert.AreEqual( "{requests}", QueueMetrics.AcknowledgedTotal.Unit );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RequeuedTotal"/> uses the correct unit "{requests}".
    /// </summary>
    [TestMethod]
    public void RequeuedTotal_HasCorrectUnit( ) {
        Assert.AreEqual( "{requests}", QueueMetrics.RequeuedTotal.Unit );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RateLimitEventsTotal"/> uses the correct unit "{events}".
    /// </summary>
    [TestMethod]
    public void RateLimitEventsTotal_HasCorrectUnit( ) {
        Assert.AreEqual( "{events}", QueueMetrics.RateLimitEventsTotal.Unit );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.DeduplicatedTotal"/> uses the correct unit "{requests}".
    /// </summary>
    [TestMethod]
    public void DeduplicatedTotal_HasCorrectUnit( ) {
        Assert.AreEqual( "{requests}", QueueMetrics.DeduplicatedTotal.Unit );
    }

    #endregion

    #region Helper Method Tests

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordEnqueue"/> records with lowercase provider and priority tag values.
    /// </summary>
    [TestMethod]
    public void RecordEnqueue_RecordsWithLowercaseProviderAndPriority( ) {
        // Arrange
        long recordedValue = 0;
        string? recordedProvider = null;
        string? recordedPriority = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.enqueued.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    recordedPriority = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordEnqueue( SupportedProviders.Spotify, QueuePriority.Interactive );

        // Assert
        Assert.AreEqual( 1, recordedValue );
        Assert.AreEqual( "spotify", recordedProvider );
        Assert.AreEqual( "interactive", recordedPriority );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordDequeue"/> records with lowercase provider and priority tag values.
    /// </summary>
    [TestMethod]
    public void RecordDequeue_RecordsWithLowercaseProviderAndPriority( ) {
        // Arrange
        long recordedValue = 0;
        string? recordedProvider = null;
        string? recordedPriority = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.dequeued.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Priority) {
                    recordedPriority = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordDequeue( SupportedProviders.AppleMusic, QueuePriority.Background );

        // Assert
        Assert.AreEqual( 1, recordedValue );
        Assert.AreEqual( "applemusic", recordedProvider );
        Assert.AreEqual( "background", recordedPriority );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordAcknowledge"/> records with lowercase provider tag value.
    /// </summary>
    [TestMethod]
    public void RecordAcknowledge_RecordsWithLowercaseProvider( ) {
        // Arrange
        long recordedValue = 0;
        string? recordedProvider = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.acknowledged.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordAcknowledge( SupportedProviders.Tidal );

        // Assert
        Assert.AreEqual( 1, recordedValue );
        Assert.AreEqual( "tidal", recordedProvider );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordRequeue"/> records with lowercase provider tag value.
    /// </summary>
    [TestMethod]
    public void RecordRequeue_RecordsWithLowercaseProvider( ) {
        // Arrange
        long recordedValue = 0;
        string? recordedProvider = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.requeued.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordRequeue( SupportedProviders.Spotify );

        // Assert
        Assert.AreEqual( 1, recordedValue );
        Assert.AreEqual( "spotify", recordedProvider );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordRateLimitEvent"/> records both the counter and histogram metrics with correct tag values.
    /// </summary>
    [TestMethod]
    public void RecordRateLimitEvent_RecordsCounterAndHistogram( ) {
        // Arrange
        long counterValue = 0;
        double histogramValue = 0;
        string? counterProvider = null;
        string? counterEndpoint = null;
        string? histogramProvider = null;
        string? histogramEndpoint = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName &&
                (instrument.Name == "bridgebeats.ratelimit.events.total" ||
                 instrument.Name == "bridgebeats.ratelimit.duration")) {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            counterValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    counterProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Endpoint) {
                    counterEndpoint = tag.Value?.ToString( );
                }
            }
        } );

        listener.SetMeasurementEventCallback<double>( ( instrument, measurement, tags, state ) => {
            histogramValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    histogramProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Endpoint) {
                    histogramEndpoint = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordRateLimitEvent( SupportedProviders.AppleMusic, "/v1/catalog/us/songs", 120.5 );

        // Assert - Counter
        Assert.AreEqual( 1, counterValue );
        Assert.AreEqual( "applemusic", counterProvider );
        Assert.AreEqual( "/v1/catalog/us/songs", counterEndpoint );

        // Assert - Histogram
        Assert.AreEqual( 120.5, histogramValue, 0.001 );
        Assert.AreEqual( "applemusic", histogramProvider );
        Assert.AreEqual( "/v1/catalog/us/songs", histogramEndpoint );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordDeduplicated"/> records the counter correctly.
    /// </summary>
    [TestMethod]
    public void RecordDeduplicated_RecordsCounter( ) {
        // Arrange
        long recordedValue = 0;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.dedup.prevented.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordDeduplicated( );

        // Assert
        Assert.AreEqual( 1, recordedValue );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordProcessingDuration"/> records with correct provider, lookup type, and status tag values.
    /// </summary>
    [TestMethod]
    public void RecordProcessingDuration_RecordsWithCorrectTags( ) {
        // Arrange
        double recordedValue = 0;
        string? recordedProvider = null;
        string? recordedLookupType = null;
        string? recordedStatus = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.queue.processing.duration") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<double>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.LookupType) {
                    recordedLookupType = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Status) {
                    recordedStatus = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordProcessingDuration(
            SupportedProviders.Tidal,
            LookupRequestType.IsrcLookup,
            "success",
            3.75
        );

        // Assert
        Assert.AreEqual( 3.75, recordedValue, 0.001 );
        Assert.AreEqual( "tidal", recordedProvider );
        Assert.AreEqual( "IsrcLookup", recordedLookupType );
        Assert.AreEqual( "success", recordedStatus );
    }

    /// <summary>
    /// Verifies that <see cref="QueueMetrics.RecordInteractiveDeferral"/> records the counter with
    /// the expected value (1), a lowercased provider tag, and the verbatim endpoint tag.
    /// </summary>
    [TestMethod]
    public void RecordInteractiveDeferral_RecordsWithLowercaseProviderAndEndpoint( ) {
        // Arrange
        long recordedValue = 0;
        string? recordedProvider = null;
        string? recordedEndpoint = null;

        using MeterListener listener = new( );
        listener.InstrumentPublished = ( instrument, meterListener ) => {
            if (instrument.Meter.Name == QueueMetrics.MeterName && instrument.Name == "bridgebeats.ratelimit.interactive_deferred.total") {
                meterListener.EnableMeasurementEvents( instrument );
            }
        };

        listener.SetMeasurementEventCallback<long>( ( instrument, measurement, tags, state ) => {
            recordedValue = measurement;
            foreach (KeyValuePair<string, object?> tag in tags) {
                if (tag.Key == QueueMetricTags.Provider) {
                    recordedProvider = tag.Value?.ToString( );
                } else if (tag.Key == QueueMetricTags.Endpoint) {
                    recordedEndpoint = tag.Value?.ToString( );
                }
            }
        } );

        listener.Start( );

        // Act
        QueueMetrics.RecordInteractiveDeferral( SupportedProviders.Spotify, "tracks" );

        // Assert
        Assert.AreEqual( 1, recordedValue );
        Assert.AreEqual( "spotify", recordedProvider );
        Assert.AreEqual( "tracks", recordedEndpoint );
    }

    #endregion
}
