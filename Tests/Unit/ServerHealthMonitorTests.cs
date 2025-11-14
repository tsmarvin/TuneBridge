using TuneBridge.Domain.Implementations.Services;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Tests.Unit {
    [TestClass]
    public class ServerHealthMonitorTests {
        [TestMethod]
        public void Constructor_ValidParameters_InitializesCorrectly( ) {
            // Arrange & Act
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Assert
            Assert.IsNotNull( monitor );
            Assert.AreEqual( 0.0, monitor.CurrentErrorRate );
            Assert.AreEqual( 0, monitor.TotalRequests );
            Assert.AreEqual( 0, monitor.TotalErrors );
        }

        [TestMethod]
        public void RecordSuccess_SingleRequest_UpdatesCounters( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Act
            monitor.RecordSuccess( );

            // Assert
            Assert.AreEqual( 1, monitor.TotalRequests );
            Assert.AreEqual( 0, monitor.TotalErrors );
            Assert.AreEqual( 0.0, monitor.CurrentErrorRate );
        }

        [TestMethod]
        public void RecordFailure_SingleRequest_UpdatesCounters( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Act
            monitor.RecordFailure( 429 );

            // Assert
            Assert.AreEqual( 1, monitor.TotalRequests );
            Assert.AreEqual( 1, monitor.TotalErrors );
        }

        [TestMethod]
        public void IsHealthy_BelowMinRequests_ReturnsTrue( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Act - record 5 failures (below min 10)
            for (int i = 0; i < 5; i++) {
                monitor.RecordFailure( 500 );
            }

            // Assert
            Assert.IsTrue( monitor.IsHealthy( ), "Should be healthy when below minimum request threshold" );
            Assert.AreEqual( 0.0, monitor.CurrentErrorRate, "Error rate should be 0 when below minimum requests" );
        }

        [TestMethod]
        public void IsHealthy_BelowErrorThreshold_ReturnsTrue( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Act - record 8 successes and 2 failures (20% error rate, below 30%)
            for (int i = 0; i < 8; i++) {
                monitor.RecordSuccess( );
            }
            for (int i = 0; i < 2; i++) {
                monitor.RecordFailure( 500 );
            }

            // Assert
            Assert.IsTrue( monitor.IsHealthy( ) );
            Assert.AreEqual( 0.2, monitor.CurrentErrorRate, 0.01 );
        }

        [TestMethod]
        public void IsHealthy_AboveErrorThreshold_ReturnsFalse( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Act - record 6 successes and 4 failures (40% error rate, above 30%)
            for (int i = 0; i < 6; i++) {
                monitor.RecordSuccess( );
            }
            for (int i = 0; i < 4; i++) {
                monitor.RecordFailure( 500 );
            }

            // Assert
            Assert.IsFalse( monitor.IsHealthy( ) );
            Assert.AreEqual( 0.4, monitor.CurrentErrorRate, 0.01 );
        }

        [TestMethod]
        public void IsHealthy_AtErrorThreshold_ReturnsTrue( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.3, 5, 10 );

            // Act - record 7 successes and 3 failures (30% error rate, exactly at threshold)
            for (int i = 0; i < 7; i++) {
                monitor.RecordSuccess( );
            }
            for (int i = 0; i < 3; i++) {
                monitor.RecordFailure( 500 );
            }

            // Assert
            Assert.IsTrue( monitor.IsHealthy( ), "Should be healthy at exactly the threshold" );
            Assert.AreEqual( 0.3, monitor.CurrentErrorRate, 0.01 );
        }

        [TestMethod]
        public void CurrentErrorRate_MixedRequests_CalculatesCorrectly( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.5, 5, 10 );

            // Act
            for (int i = 0; i < 15; i++) {
                monitor.RecordSuccess( );
            }
            for (int i = 0; i < 5; i++) {
                monitor.RecordFailure( 429 );
            }

            // Assert
            Assert.AreEqual( 20, monitor.TotalRequests );
            Assert.AreEqual( 5, monitor.TotalErrors );
            Assert.AreEqual( 0.25, monitor.CurrentErrorRate, 0.01 );
        }

        [TestMethod]
        public void Constructor_ClampsMaxErrorRate_ToValidRange( ) {
            // Arrange & Act - test with values outside 0.0-1.0 range
            ServerHealthMonitor monitor1 = new( -0.5, 5, 10 );
            ServerHealthMonitor monitor2 = new( 1.5, 5, 10 );

            // Add enough requests to test the clamping
            for (int i = 0; i < 10; i++) {
                monitor1.RecordFailure( 500 );
                monitor2.RecordSuccess( );
            }

            // Assert - monitor1 should clamp to 0.0 (always unhealthy with any errors)
            Assert.IsFalse( monitor1.IsHealthy( ), "Clamped to 0.0, should be unhealthy with errors" );

            // monitor2 should clamp to 1.0 (always healthy)
            Assert.IsTrue( monitor2.IsHealthy( ), "Clamped to 1.0, should always be healthy" );
        }

        [TestMethod]
        public void RecordFailure_RecordsStatusCode( ) {
            // Arrange
            ServerHealthMonitor monitor = new( 0.5, 5, 10 );

            // Act
            monitor.RecordFailure( 429 );
            monitor.RecordFailure( 503 );

            // Assert - just verify it doesn't throw and updates counters
            Assert.AreEqual( 2, monitor.TotalErrors );
        }
    }
}
