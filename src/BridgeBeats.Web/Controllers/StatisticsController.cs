using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for displaying lookup collection statistics.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="StatisticsController"/> class.
/// </remarks>
/// <param name="statisticsService">Optional statistics service (null if not configured).</param>
/// <param name="logger">Logger for diagnostic information.</param>
[Authorize]
public partial class StatisticsController(
    IStatisticsService? statisticsService,
    ILogger<StatisticsController> logger
) : Controller {

    /// <summary>
    /// Displays the statistics page with lookup collection metrics.
    /// </summary>
    /// <returns>The statistics view.</returns>
    public async Task<IActionResult> Index( CancellationToken cancellationToken ) {
        if (statisticsService is null) {
            LogNotAvailable( );
            return View( "Error", new ErrorViewModel { Message = "Statistics service is not configured." } );
        }

        try {
            LookupStatistics stats = await statisticsService.GetStatisticsAsync( cancellationToken );
            return View( stats );
        } catch (OperationCanceledException) {
            throw; // Let the framework handle cancellation
        } catch (Exception ex) {
            LogRetrieveError( ex );
            return View( "Error", new ErrorViewModel { Message = "Unable to retrieve statistics. Please try again later." } );
        }
    }

    /// <summary>
    /// Forces a refresh of the cached statistics.
    /// </summary>
    /// <returns>Redirect to the statistics page.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize( Roles = Roles.AspireDashboardAccess )]
    public async Task<IActionResult> Refresh( CancellationToken cancellationToken ) {
        if (statisticsService is null) {
            return RedirectToAction( nameof( Index ) );
        }

        try {
            _ = await statisticsService.RefreshStatisticsAsync( cancellationToken );
        } catch (Exception ex) {
            LogRefreshError( ex );
        }

        return RedirectToAction( nameof( Index ) );
    }
}
