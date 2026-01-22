using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Controllers;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StatisticsController"/> to verify view rendering,
/// error handling, and refresh functionality.
/// </summary>
[TestClass]
public class StatisticsControllerTests {

    private Mock<IStatisticsService> _statisticsServiceMock = null!;
    private Mock<ILogger<StatisticsController>> _loggerMock = null!;

    /// <summary>
    /// Initializes mocks before each test.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _statisticsServiceMock = new Mock<IStatisticsService>( );
        _loggerMock = new Mock<ILogger<StatisticsController>>( );
    }

    #region Index Tests

    /// <summary>
    /// Verifies that Index returns a view with statistics when service is valid.
    /// </summary>
    [TestMethod]
    public async Task Index_WithValidService_ShouldReturnViewWithStatistics( ) {
        // Arrange
        LookupStatistics expectedStats = CreateTestStatistics( );
        _ = _statisticsServiceMock
            .Setup( x => x.GetStatisticsAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( expectedStats );

        StatisticsController controller = CreateController( );

        // Act
        IActionResult result = await controller.Index( CancellationToken.None );

        // Assert
        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        LookupStatistics model = Assert.IsInstanceOfType<LookupStatistics>( viewResult.Model );
        Assert.AreEqual( 100, model.TotalRecords );
    }

    /// <summary>
    /// Verifies that Index returns an error view when service is null.
    /// </summary>
    [TestMethod]
    public async Task Index_WithNullService_ShouldReturnErrorView( ) {
        // Arrange
        StatisticsController controller = new(
            null, // No statistics service
            _loggerMock.Object
        );

        // Act
        IActionResult result = await controller.Index( CancellationToken.None );

        // Assert
        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        Assert.AreEqual( "Error", viewResult.ViewName );
        ErrorViewModel model = Assert.IsInstanceOfType<ErrorViewModel>( viewResult.Model );
        Assert.IsNotNull( model.Message );
        Assert.Contains( "not configured", model.Message );
    }

    /// <summary>
    /// Verifies that Index returns an error view when service throws an exception.
    /// </summary>
    [TestMethod]
    public async Task Index_WhenServiceThrows_ShouldReturnErrorView( ) {
        // Arrange
        _ = _statisticsServiceMock
            .Setup( x => x.GetStatisticsAsync( It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );

        StatisticsController controller = CreateController( );

        // Act
        IActionResult result = await controller.Index( CancellationToken.None );

        // Assert
        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        Assert.AreEqual( "Error", viewResult.ViewName );
        ErrorViewModel model = Assert.IsInstanceOfType<ErrorViewModel>( viewResult.Model );
        Assert.IsNotNull( model.Message );
        Assert.Contains( "Unable to retrieve", model.Message );
    }

    /// <summary>
    /// Verifies that Index throws <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    [TestMethod]
    public async Task Index_WhenCancelled_ShouldThrowOperationCanceledException( ) {
        // Arrange
        using CancellationTokenSource cts = new( );
        await cts.CancelAsync( );

        _ = _statisticsServiceMock
            .Setup( x => x.GetStatisticsAsync( It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new OperationCanceledException( ) );

        StatisticsController controller = CreateController( );

        // Act & Assert
        _ = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            async ( ) => await controller.Index( cts.Token )
        );
    }

    #endregion

    #region Refresh Tests

    /// <summary>
    /// Verifies that Refresh calls RefreshStatisticsAsync and redirects to Index.
    /// </summary>
    [TestMethod]
    public async Task Refresh_WithValidService_ShouldCallRefreshAndRedirect( ) {
        // Arrange
        _ = _statisticsServiceMock
            .Setup( x => x.RefreshStatisticsAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( CreateTestStatistics( ) );

        StatisticsController controller = CreateController( );

        // Act
        IActionResult result = await controller.Refresh( CancellationToken.None );

        // Assert
        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
        _statisticsServiceMock.Verify(
            x => x.RefreshStatisticsAsync( It.IsAny<CancellationToken>( ) ),
            Times.Once( )
        );
    }

    /// <summary>
    /// Verifies that Refresh redirects to Index even when service is null.
    /// </summary>
    [TestMethod]
    public async Task Refresh_WithNullService_ShouldRedirectWithoutError( ) {
        // Arrange
        StatisticsController controller = new(
            null, // No statistics service
            _loggerMock.Object
        );

        // Act
        IActionResult result = await controller.Refresh( CancellationToken.None );

        // Assert
        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
    }

    /// <summary>
    /// Verifies that Refresh redirects to Index even when service throws an exception.
    /// </summary>
    [TestMethod]
    public async Task Refresh_WhenServiceThrows_ShouldStillRedirect( ) {
        // Arrange
        _ = _statisticsServiceMock
            .Setup( x => x.RefreshStatisticsAsync( It.IsAny<CancellationToken>( ) ) )
            .ThrowsAsync( new InvalidOperationException( "Test error" ) );

        StatisticsController controller = CreateController( );

        // Act
        IActionResult result = await controller.Refresh( CancellationToken.None );

        // Assert - Should redirect even on error
        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a <see cref="StatisticsController"/> instance with the mocked dependencies.
    /// </summary>
    /// <returns>A configured <see cref="StatisticsController"/> instance.</returns>
    private StatisticsController CreateController( ) {
        return new StatisticsController(
            _statisticsServiceMock.Object,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Creates a test <see cref="LookupStatistics"/> instance with sample data.
    /// </summary>
    /// <returns>A populated <see cref="LookupStatistics"/> instance.</returns>
    private static LookupStatistics CreateTestStatistics( ) {
        return new LookupStatistics {
            TotalRecords = 100,
            AlbumCount = 25,
            TrackCount = 75,
            ProviderCounts = new Dictionary<string, int> {
                ["Spotify"] = 90,
                ["AppleMusic"] = 85,
                ["Tidal"] = 50
            },
            RecentEntries = [
                new RecentLookupEntry {
                    AtUri = "at://test/1",
                    IsAlbum = false,
                    Artist = "Test Artist",
                    Title = "Test Track",
                    LookedUpAt = DateTimeOffset.UtcNow,
                    CardId = "abc123"
                }
            ],
            EarliestLookup = DateTimeOffset.UtcNow.AddDays( -30 ),
            LatestLookup = DateTimeOffset.UtcNow,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
