using System.Diagnostics.Metrics;
using BridgeBeats.Infrastructure.Queue;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="QueueMetrics"/> and <see cref="QueueMetricTags"/>.
/// Validates metric definitions, tag constants, and instrumentation correctness.
/// </summary>
[TestClass]
public class QueueMetricsTests {

    #region Meter Tests

    [TestMethod]
    public void Meter_HasCorrectName( ) {
        // Arrange
        const string expectedName = "BridgeBeats.Queue";

        // Assert
        Assert.AreEqual( expectedName, QueueMetrics.MeterName );
        Assert.AreEqual( expectedName, QueueMetrics.Meter.Name );
    }

    [TestMethod]
    public void Meter_HasCorrectVersion( ) {
        // Assert
        Assert.AreEqual( "1.0.0", QueueMetrics.Meter.Version );
    }

    #endregion

    #region Counter Tests

    [TestMethod]
    public void EnqueuedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.EnqueuedTotal );
    }

    [TestMethod]
    public void EnqueuedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.enqueued.total", QueueMetrics.EnqueuedTotal.Name );
    }

    [TestMethod]
    public void DequeuedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.DequeuedTotal );
    }

    [TestMethod]
    public void DequeuedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.dequeued.total", QueueMetrics.DequeuedTotal.Name );
    }

    [TestMethod]
    public void AcknowledgedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.AcknowledgedTotal );
    }

    [TestMethod]
    public void AcknowledgedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.acknowledged.total", QueueMetrics.AcknowledgedTotal.Name );
    }

    [TestMethod]
    public void RequeuedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.RequeuedTotal );
    }

    [TestMethod]
    public void RequeuedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.requeued.total", QueueMetrics.RequeuedTotal.Name );
    }

    [TestMethod]
    public void RateLimitEventsTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.RateLimitEventsTotal );
    }

    [TestMethod]
    public void RateLimitEventsTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.ratelimit.events.total", QueueMetrics.RateLimitEventsTotal.Name );
    }

    [TestMethod]
    public void DeduplicatedTotal_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.DeduplicatedTotal );
    }

    [TestMethod]
    public void DeduplicatedTotal_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.dedup.prevented.total", QueueMetrics.DeduplicatedTotal.Name );
    }

    #endregion

    #region Histogram Tests

    [TestMethod]
    public void ProcessingDuration_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.ProcessingDuration );
    }

    [TestMethod]
    public void ProcessingDuration_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.queue.processing.duration", QueueMetrics.ProcessingDuration.Name );
    }

    [TestMethod]
    public void ProcessingDuration_HasCorrectUnit( ) {
        // Assert
        Assert.AreEqual( "s", QueueMetrics.ProcessingDuration.Unit );
    }

    [TestMethod]
    public void RateLimitDuration_IsNotNull( ) {
        // Assert
        Assert.IsNotNull( QueueMetrics.RateLimitDuration );
    }

    [TestMethod]
    public void RateLimitDuration_HasCorrectName( ) {
        // Assert
        Assert.AreEqual( "bridgebeats.ratelimit.duration", QueueMetrics.RateLimitDuration.Name );
    }

    [TestMethod]
    public void RateLimitDuration_HasCorrectUnit( ) {
        // Assert
        Assert.AreEqual( "s", QueueMetrics.RateLimitDuration.Unit );
    }

    #endregion

    #region Counter Recording Tests

    [TestMethod]
    public void EnqueuedTotal_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        try {
            QueueMetrics.EnqueuedTotal.Add( 1,
                new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
                new KeyValuePair<string, object?>( QueueMetricTags.Priority, "interactive" )
            );
        } catch (Exception ex) {
            Assert.Fail( $"Recording metric should not throw exception: {ex.Message}" );
        }

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.EnqueuedTotal );
    }

    [TestMethod]
    public void DequeuedTotal_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        try {
            QueueMetrics.DequeuedTotal.Add( 1,
                new KeyValuePair<string, object?>( QueueMetricTags.Provider, "applemusic" ),
                new KeyValuePair<string, object?>( QueueMetricTags.Priority, "background" )
            );
        } catch (Exception ex) {
            Assert.Fail( $"Recording metric should not throw exception: {ex.Message}" );
        }

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.DequeuedTotal );
    }

    [TestMethod]
    public void RateLimitEventsTotal_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        try {
            QueueMetrics.RateLimitEventsTotal.Add( 1,
                new KeyValuePair<string, object?>( QueueMetricTags.Provider, "tidal" ),
                new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, "/v1/tracks" )
            );
        } catch (Exception ex) {
            Assert.Fail( $"Recording metric should not throw exception: {ex.Message}" );
        }

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.RateLimitEventsTotal );
    }

    [TestMethod]
    public void DeduplicatedTotal_CanRecordWithoutTags( ) {
        // Arrange & Act - Should not throw
        try {
            QueueMetrics.DeduplicatedTotal.Add( 1 );
        } catch (Exception ex) {
            Assert.Fail( $"Recording metric should not throw exception: {ex.Message}" );
        }

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.DeduplicatedTotal );
    }

    #endregion

    #region Histogram Recording Tests

    [TestMethod]
    public void ProcessingDuration_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        try {
            QueueMetrics.ProcessingDuration.Record( 1.5,
                new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
                new KeyValuePair<string, object?>( QueueMetricTags.LookupType, "IsrcLookup" ),
                new KeyValuePair<string, object?>( QueueMetricTags.Status, "success" )
            );
        } catch (Exception ex) {
            Assert.Fail( $"Recording metric should not throw exception: {ex.Message}" );
        }

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.ProcessingDuration );
    }

    [TestMethod]
    public void RateLimitDuration_CanRecordWithTags( ) {
        // Arrange & Act - Should not throw
        try {
            QueueMetrics.RateLimitDuration.Record( 60.0,
                new KeyValuePair<string, object?>( QueueMetricTags.Provider, "spotify" ),
                new KeyValuePair<string, object?>( QueueMetricTags.Endpoint, "/v1/albums" )
            );
        } catch (Exception ex) {
            Assert.Fail( $"Recording metric should not throw exception: {ex.Message}" );
        }

        // Assert - No exception thrown means success
        Assert.IsNotNull( QueueMetrics.RateLimitDuration );
    }

    #endregion

    #region QueueMetricTags Tests

    [TestMethod]
    public void QueueMetricTags_Provider_HasCorrectValue( ) {
        // Arrange
        const string expected = "provider";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.Provider );
    }

    [TestMethod]
    public void QueueMetricTags_Priority_HasCorrectValue( ) {
        // Arrange
        const string expected = "priority";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.Priority );
    }

    [TestMethod]
    public void QueueMetricTags_Endpoint_HasCorrectValue( ) {
        // Arrange
        const string expected = "endpoint";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.Endpoint );
    }

    [TestMethod]
    public void QueueMetricTags_LookupType_HasCorrectValue( ) {
        // Arrange
        const string expected = "lookup_type";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.LookupType );
    }

    [TestMethod]
    public void QueueMetricTags_Status_HasCorrectValue( ) {
        // Arrange
        const string expected = "status";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.Status );
    }

    [TestMethod]
    public void QueueMetricTags_StatusCode_HasCorrectValue( ) {
        // Arrange
        const string expected = "status_code";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.StatusCode );
    }

    [TestMethod]
    public void QueueMetricTags_Method_HasCorrectValue( ) {
        // Arrange
        const string expected = "method";

        // Assert
        Assert.AreEqual( expected, QueueMetricTags.Method );
    }

    [TestMethod]
    public void QueueMetricTags_AllTagsAreNonNullAndNonEmpty( ) {
        // Assert - Validate all tag constants are usable
        Assert.IsNotNull( QueueMetricTags.Provider );
        Assert.IsTrue( QueueMetricTags.Provider.Length > 0 );

        Assert.IsNotNull( QueueMetricTags.Priority );
        Assert.IsTrue( QueueMetricTags.Priority.Length > 0 );

        Assert.IsNotNull( QueueMetricTags.Endpoint );
        Assert.IsTrue( QueueMetricTags.Endpoint.Length > 0 );

        Assert.IsNotNull( QueueMetricTags.LookupType );
        Assert.IsTrue( QueueMetricTags.LookupType.Length > 0 );

        Assert.IsNotNull( QueueMetricTags.Status );
        Assert.IsTrue( QueueMetricTags.Status.Length > 0 );

        Assert.IsNotNull( QueueMetricTags.StatusCode );
        Assert.IsTrue( QueueMetricTags.StatusCode.Length > 0 );

        Assert.IsNotNull( QueueMetricTags.Method );
        Assert.IsTrue( QueueMetricTags.Method.Length > 0 );
    }
        Assert.IsFalse( string.IsNullOrEmpty( QueueMetricTags.StatusCode ) );
        Assert.IsFalse( string.IsNullOrEmpty( QueueMetricTags.Method ) );
    }

    #endregion

    #region MeterListener Verification Tests

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
}
