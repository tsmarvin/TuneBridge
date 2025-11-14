using Microsoft.Extensions.Logging;
using Moq;
using TuneBridge.Domain.Implementations.Services;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Tests.Integration {
    [TestClass]
    public class JetstreamMonitorServiceTests {
        [TestMethod]
        public void Constructor_ValidParameters_InitializesCorrectly( ) {
            // Arrange
            Mock<ILogger<JetstreamMonitorService>> mockLogger = new( );
            Mock<IMediaLinkService> mockMediaLinkService = new( );
            Mock<IServerHealthMonitor> mockHealthMonitor = new( );

            // Act
            JetstreamMonitorService service = new(
                mockLogger.Object,
                mockMediaLinkService.Object,
                mockHealthMonitor.Object,
                "wss://jetstream2.us-east.bsky.network/subscribe",
                "did:plc:test123",
                isEnabled: true
            );

            // Assert
            Assert.IsNotNull( service );
        }

        [TestMethod]
        public async Task StartAsync_WhenDisabled_DoesNotConnect( ) {
            // Arrange
            Mock<ILogger<JetstreamMonitorService>> mockLogger = new( );
            Mock<IMediaLinkService> mockMediaLinkService = new( );
            Mock<IServerHealthMonitor> mockHealthMonitor = new( );

            JetstreamMonitorService service = new(
                mockLogger.Object,
                mockMediaLinkService.Object,
                mockHealthMonitor.Object,
                "wss://jetstream2.us-east.bsky.network/subscribe",
                "did:plc:test123",
                isEnabled: false
            );

            // Act
            await service.StartAsync( CancellationToken.None );

            // Assert - should complete without error
            // Verify it logged that it's disabled
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>( ),
                    It.Is<It.IsAnyType>( ( v, t ) => v.ToString( )!.Contains( "disabled" ) ),
                    It.IsAny<Exception?>( ),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>( )
                ),
                Times.Once
            );
        }

        [TestMethod]
        public async Task StopAsync_WhenDisabled_CompletesSuccessfully( ) {
            // Arrange
            Mock<ILogger<JetstreamMonitorService>> mockLogger = new( );
            Mock<IMediaLinkService> mockMediaLinkService = new( );
            Mock<IServerHealthMonitor> mockHealthMonitor = new( );

            JetstreamMonitorService service = new(
                mockLogger.Object,
                mockMediaLinkService.Object,
                mockHealthMonitor.Object,
                "wss://jetstream2.us-east.bsky.network/subscribe",
                "did:plc:test123",
                isEnabled: false
            );

            await service.StartAsync( CancellationToken.None );

            // Act & Assert - should complete without error
            await service.StopAsync( CancellationToken.None );
        }

        [TestMethod]
        public void Dispose_DisposesResourcesCleanly( ) {
            // Arrange
            Mock<ILogger<JetstreamMonitorService>> mockLogger = new( );
            Mock<IMediaLinkService> mockMediaLinkService = new( );
            Mock<IServerHealthMonitor> mockHealthMonitor = new( );

            JetstreamMonitorService service = new(
                mockLogger.Object,
                mockMediaLinkService.Object,
                mockHealthMonitor.Object,
                "wss://jetstream2.us-east.bsky.network/subscribe",
                "did:plc:test123",
                isEnabled: false
            );

            // Act & Assert - should not throw
            service.Dispose( );
        }
    }
}
