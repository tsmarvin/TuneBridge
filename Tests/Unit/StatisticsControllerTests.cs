using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Controllers;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="StatisticsController"/>, the MVC controller that renders the lookup
/// statistics page and triggers refreshes. Covers the Index action across its branches (cached
/// stats, no cached stats / generating view, null service / error view, live bootstrap status
/// overlay, and absent bootstrap status) and the Refresh action's redirect behavior whether or not
/// a refresh was already running.
/// </summary>
[TestClass]
public class StatisticsControllerTests {

    /// <summary>Mock statistics service the controller reads cached stats and bootstrap status from.</summary>
    private Mock<IStatisticsService> _statisticsServiceMock = null!;
    /// <summary>Mock logger for the controller.</summary>
    private Mock<ILogger<StatisticsController>> _loggerMock = null!;

    /// <summary>
    /// Creates fresh mocks before each test and defaults the live bootstrap status to null so tests
    /// that do not opt into an overlay see no bootstrap status.
    /// </summary>
    [TestInitialize]
    public void Initialize( ) {
        _statisticsServiceMock = new Mock<IStatisticsService>( );
        _loggerMock = new Mock<ILogger<StatisticsController>>( );

        // Default: live status returns null (no bootstrap has run yet)
        _ = _statisticsServiceMock
            .Setup( x => x.GetLiveBootstrapStatusAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( (CacheBootstrapStatus?)null );
    }

    /// <summary>
    /// Verifies that when cached statistics exist and no refresh is in progress, Index returns the
    /// default view with the statistics model and an <c>IsRefreshing</c> view-data flag of false.
    /// </summary>
    [TestMethod]
    public async Task Index_WithCachedStats_ReturnsViewWithStats( ) {
        LookupStatistics expectedStats = CreateTestStatistics( );
        _ = _statisticsServiceMock
            .Setup( x => x.GetCachedStatistics( ) )
            .Returns( expectedStats );
        _ = _statisticsServiceMock
            .Setup( x => x.IsRefreshing )
            .Returns( false );

        StatisticsController controller = CreateController( );

        IActionResult result = await controller.Index( );

        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        LookupStatistics model = Assert.IsInstanceOfType<LookupStatistics>( viewResult.Model );
        Assert.AreEqual( 100, model.TotalRecords );
        Assert.IsFalse( (bool)(viewResult.ViewData["IsRefreshing"] ?? false) );
    }

    /// <summary>
    /// Verifies that when no cached statistics exist, Index returns the <c>Generating</c> view with
    /// the <c>IsRefreshing</c> flag set to true, signalling the page that stats are being built.
    /// </summary>
    [TestMethod]
    public async Task Index_WithNoCachedStats_ReturnsGeneratingView( ) {
        _ = _statisticsServiceMock
            .Setup( x => x.GetCachedStatistics( ) )
            .Returns( (LookupStatistics?)null );

        StatisticsController controller = CreateController( );

        IActionResult result = await controller.Index( );

        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        Assert.AreEqual( "Generating", viewResult.ViewName );
        Assert.IsTrue( (bool)(viewResult.ViewData["IsRefreshing"] ?? false) );
    }

    /// <summary>
    /// Verifies that when the controller was constructed without a statistics service, Index returns
    /// the <c>Error</c> view with an <see cref="ErrorViewModel"/> whose message reports the service
    /// is not configured.
    /// </summary>
    [TestMethod]
    public async Task Index_WithNullService_ReturnsErrorView( ) {
        StatisticsController controller = new(
            null,
            _loggerMock.Object
        );

        IActionResult result = await controller.Index( );

        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        Assert.AreEqual( "Error", viewResult.ViewName );
        ErrorViewModel model = Assert.IsInstanceOfType<ErrorViewModel>( viewResult.Model );
        Assert.IsNotNull( model.Message );
        Assert.Contains( "not configured", model.Message );
    }

    /// <summary>
    /// Verifies that a live cache-bootstrap status is overlaid onto the cached statistics model: the
    /// returned model carries the running flag and last-success count from the live status while the
    /// cached totals (record count) remain unchanged.
    /// </summary>
    [TestMethod]
    public async Task Index_WithLiveBootstrapStatus_OverlaysStatusOntoCachedStats( ) {
        // Arrange: cached stats have no bootstrap status; live status has a running flag
        LookupStatistics cachedStats = CreateTestStatistics( );
        CacheBootstrapStatus liveStatus = new( ) {
            IsRunning = true,
            LastRunTime = DateTimeOffset.UtcNow.AddHours( -1 ),
            LastSuccessCount = 247404
        };

        _ = _statisticsServiceMock
            .Setup( x => x.GetCachedStatistics( ) )
            .Returns( cachedStats );
        _ = _statisticsServiceMock
            .Setup( x => x.IsRefreshing )
            .Returns( false );
        _ = _statisticsServiceMock
            .Setup( x => x.GetLiveBootstrapStatusAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( liveStatus );

        StatisticsController controller = CreateController( );

        // Act
        IActionResult result = await controller.Index( );

        // Assert: model reflects live status, not cached status
        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        LookupStatistics model = Assert.IsInstanceOfType<LookupStatistics>( viewResult.Model );
        Assert.IsNotNull( model.CacheBootstrapStatus );
        Assert.IsTrue( model.CacheBootstrapStatus.IsRunning );
        Assert.AreEqual( 247404, model.CacheBootstrapStatus.LastSuccessCount );
        // Other cached stats fields are preserved
        Assert.AreEqual( 100, model.TotalRecords );
    }

    /// <summary>
    /// Verifies that when no live bootstrap status is available, the returned model's bootstrap
    /// status is null while the cached totals are still surfaced.
    /// </summary>
    [TestMethod]
    public async Task Index_WhenLiveStatusReturnsNull_ModelHasNullBootstrapStatus( ) {
        // Arrange: live status unavailable (Redis failure or no prior run)
        LookupStatistics cachedStats = CreateTestStatistics( );

        _ = _statisticsServiceMock
            .Setup( x => x.GetCachedStatistics( ) )
            .Returns( cachedStats );
        _ = _statisticsServiceMock
            .Setup( x => x.IsRefreshing )
            .Returns( false );
        _ = _statisticsServiceMock
            .Setup( x => x.GetLiveBootstrapStatusAsync( It.IsAny<CancellationToken>( ) ) )
            .ReturnsAsync( (CacheBootstrapStatus?)null );

        StatisticsController controller = CreateController( );

        // Act: should not throw even when live status is null
        IActionResult result = await controller.Index( );

        // Assert: page renders successfully with null bootstrap status
        ViewResult viewResult = Assert.IsInstanceOfType<ViewResult>( result );
        LookupStatistics model = Assert.IsInstanceOfType<LookupStatistics>( viewResult.Model );
        Assert.IsNull( model.CacheBootstrapStatus );
        Assert.AreEqual( 100, model.TotalRecords );
    }

    /// <summary>
    /// Verifies that the Refresh action calls the service to trigger a refresh and immediately
    /// redirects to Index without waiting for the refresh to complete.
    /// </summary>
    [TestMethod]
    public void Refresh_TriggersServiceRefresh_RedirectsImmediately( ) {
        _ = _statisticsServiceMock
            .Setup( x => x.TriggerRefresh( ) )
            .Returns( true );

        StatisticsController controller = CreateController( );

        IActionResult result = controller.Refresh( );

        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
        _statisticsServiceMock.Verify( x => x.TriggerRefresh( ), Times.Once( ) );
    }

    /// <summary>
    /// Verifies that the Refresh action still redirects to Index when a refresh is already running
    /// (the service reports false), so a concurrent trigger is a harmless no-op for the user.
    /// </summary>
    [TestMethod]
    public void Refresh_WhenRefreshAlreadyRunning_StillRedirects( ) {
        _ = _statisticsServiceMock
            .Setup( x => x.TriggerRefresh( ) )
            .Returns( false );

        StatisticsController controller = CreateController( );

        IActionResult result = controller.Refresh( );

        RedirectToActionResult redirectResult = Assert.IsInstanceOfType<RedirectToActionResult>( result );
        Assert.AreEqual( "Index", redirectResult.ActionName );
        _statisticsServiceMock.Verify( x => x.TriggerRefresh( ), Times.Once( ) );
    }

    /// <summary>
    /// Builds a controller wired to the mock statistics service and logger.
    /// </summary>
    /// <returns>A controller under test.</returns>
    private StatisticsController CreateController( ) {
        return new StatisticsController(
            _statisticsServiceMock.Object,
            _loggerMock.Object
        );
    }

    /// <summary>
    /// Builds a representative <see cref="LookupStatistics"/> fixture (100 records, sample provider
    /// counts, one recent entry, and populated timestamps) used as the cached-stats payload.
    /// </summary>
    /// <returns>A populated statistics model for assertions.</returns>
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
