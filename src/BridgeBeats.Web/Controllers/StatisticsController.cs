using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Serves the lookup statistics page and an administrator-triggered refresh. Reads cached statistics,
/// overlays the live cache-bootstrap status, and indicates when a refresh is in progress. Requires an
/// authenticated session.
/// </summary>
/// <param name="statisticsService">Optional statistics service that supplies cached statistics, live bootstrap status, and refresh control; when null, the page reports the service is not configured.</param>
/// <param name="logger">Logger for statistics availability and refresh errors.</param>
[Authorize]
public partial class StatisticsController(
    IStatisticsService? statisticsService,
    ILogger<StatisticsController> logger
) : Controller {

    /// <summary>
    /// Renders the statistics page, combining cached statistics with the live cache-bootstrap status. Shows a
    /// "generating" view when no statistics are cached yet.
    /// </summary>
    /// <returns>
    /// The statistics view with the combined data on success; the <c>Generating</c> view while statistics are
    /// being produced; the <c>Error</c> view when the service is not configured or retrieval fails. Requires
    /// an authenticated session via <c>[Authorize]</c>.
    /// </returns>
    public async Task<IActionResult> Index( ) {
        if (statisticsService is null) {
            LogNotAvailable( );
            return View( "Error", new ErrorViewModel { Message = "Statistics service is not configured." } );
        }

        try {
            LookupStatistics? stats = statisticsService.GetCachedStatistics( );

            if (stats is null) {
                ViewBag.IsRefreshing = true;
                return View( "Generating" );
            }

            // Overlay live bootstrap status so the page reflects the current run state
            // without waiting for the full statistics cache to expire.
            CacheBootstrapStatus? liveStatus =
                await statisticsService.GetLiveBootstrapStatusAsync( HttpContext?.RequestAborted ?? CancellationToken.None );
            LookupStatistics displayStats = stats.WithBootstrapStatus( liveStatus );

            ViewBag.IsRefreshing = statisticsService.IsRefreshing;
            return View( displayStats );
        } catch (Exception ex) {
            LogRetrieveError( ex );
            return View( "Error", new ErrorViewModel { Message = "Unable to retrieve statistics. Please try again later." } );
        }
    }

    /// <summary>
    /// Triggers a background refresh of the cached statistics, then redirects to the statistics page.
    /// Restricted to users in the <see cref="BridgeBeats.Contracts.Constants.Roles.AspireDashboardAccess"/> role.
    /// </summary>
    /// <returns>
    /// HTTP POST. A redirect to <see cref="Index"/>. Requires the dashboard-access role and a valid
    /// anti-forgery token.
    /// </returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize( Roles = Roles.AspireDashboardAccess )]
    public IActionResult Refresh( ) {
        if (statisticsService is null) {
            return RedirectToAction( nameof( Index ) );
        }

        try {
            _ = statisticsService.TriggerRefresh( );
        } catch (Exception ex) {
            LogRefreshError( ex );
        }

        return RedirectToAction( nameof( Index ) );
    }
}
