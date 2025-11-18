using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Common.Contracts.Constants;
using TuneBridge.Web.Models;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for Aspire Dashboard authorization checks.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DashboardController"/> class.
/// </remarks>
/// <param name="userManager">The user manager.</param>
/// <param name="logger">The logger.</param>
[ApiController]
[Route( "api/[controller]" )]
public class DashboardController(
    UserManager<ApplicationUser> userManager,
    ILogger<DashboardController> logger
) : ControllerBase {

    /// <summary>
    /// Checks if the current user has access to the Aspire Dashboard.
    /// This endpoint is used by Caddy's forward_auth directive.
    /// </summary>
    /// <returns>200 OK if authorized, 401/403 if not authorized.</returns>
    [HttpGet( "authorize" )]
    [Authorize]
    public async Task<IActionResult> Authorize( ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );

        if (user == null) {
            logger.LogWarning( "Dashboard authorization failed: User not found" );
            return Unauthorized( new { error = "User not authenticated" } );
        }

        bool hasAccess = await userManager.IsInRoleAsync( user, Roles.AspireDashboardAccess );

        if (!hasAccess) {
            logger.LogWarning(
                "Dashboard authorization denied for user {UserId} - missing {Role} role",
                user.Id,
                Roles.AspireDashboardAccess
            );
            return StatusCode( 403, new { error = "Access denied: AspireDashboardAccess role required" } );
        }

        logger.LogInformation( "Dashboard authorization granted for user {UserId}", user.Id );
        return Ok( new { authorized = true } );
    }
}
