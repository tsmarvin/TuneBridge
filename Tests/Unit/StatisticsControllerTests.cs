using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Controllers;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

[TestClass]
public class StatisticsControllerTests {

    private Mock<IStatisticsService> _statisticsServiceMock = null!;
    private Mock<ILogger<StatisticsController>> _loggerMock = null!;

    [TestInitialize]
    public void Initialize( ) {
        _statisticsServiceMock = new Mock<IStatisticsService>( );
        _loggerMock = new Mock<ILogger<StatisticsController>>( );
    }

    [TestMethod]
    public void Index_WithCachedStats_ReturnsViewWithStats( ) {
        LookupStatistics expectedStats = CreateTestStatistics( );
        _ = _statisticsServiceMock
            .Setup( x => x.GetCachedStatistics( ) )
            .Returns( expectedStats );
        _ = _statisticsServiceMock
            .Setup( x => x.IsRefreshing )
            .Returns( false );

        StatisticsController controller = CreateController( );

        IActionResult result = controller.Index();

        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        LookupStatistics model = Assert.IsInstanceOfType<LookupStatistics>( viewResult.Model );
        Assert.AreEqual( 100, model.TotalRecords );
        Assert.IsFalse( (bool)(viewResult.ViewData["IsRefreshing"] ?? false) );
    }

    [TestMethod]
    public void Index_WithNoCachedStats_ReturnsGeneratingView( ) {
        _ = _statisticsServiceMock
            .Setup( x => x.GetCachedStatistics( ) )
            .Returns( (LookupStatistics?)null );

        StatisticsController controller = CreateController();

        IActionResult result = controller.Index();

        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        Assert.AreEqual( "Generating", viewResult.ViewName );
        Assert.IsTrue( (bool)(viewResult.ViewData["IsRefreshing"] ?? false) );
    }

    [TestMethod]
    public void Index_WithNullService_ReturnsErrorView( ) {
        StatisticsController controller = new(
            null,
            _loggerMock.Object
        );

        IActionResult result = controller.Index();

        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        Assert.AreEqual( "Error", viewResult.ViewName );
        ErrorViewModel model = Assert.IsInstanceOfType<ErrorViewModel>( viewResult.Model );
        Assert.IsNotNull( model.Message );
        Assert.Contains( "not configured", model.Message );
    }

    [TestMethod]
    public void Refresh_TriggersServiceRefresh_RedirectsImmediately( ) {
        _ = _statisticsServiceMock
            .Setup( x => x.TriggerRefresh( ) )
            .Returns( true );

        StatisticsController controller = CreateController( );

        IActionResult result = controller.Refresh();

        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
        _statisticsServiceMock.Verify( x => x.TriggerRefresh( ), Times.Once( ) );
    }

    [TestMethod]
    public void Refresh_WhenRefreshAlreadyRunning_StillRedirects( ) {
        _ = _statisticsServiceMock
            .Setup( x => x.TriggerRefresh( ) )
            .Returns( false );

        StatisticsController controller = CreateController( );

        IActionResult result = controller.Refresh();

        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
        _statisticsServiceMock.Verify( x => x.TriggerRefresh( ), Times.Once( ) );
    }

    private StatisticsController CreateController( ) {
        return new StatisticsController(
            _statisticsServiceMock.Object,
            _loggerMock.Object
        );
    }

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
}

#pragma warning restore CS1591
