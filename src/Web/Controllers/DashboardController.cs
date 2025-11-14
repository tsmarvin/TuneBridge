using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Models;
using TuneBridge.Domain.Types.Constants;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for Aspire Dashboard authorization checks.
/// </summary>
[ApiController]
[Route( "api/[controller]" )]
public class DashboardController : ControllerBase {
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<DashboardController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardController"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="logger">The logger.</param>
    public DashboardController(
        UserManager<ApplicationUser> userManager,
        ILogger<DashboardController> logger
    ) {
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Checks if the current user has access to the Aspire Dashboard.
    /// This endpoint is used by Caddy's forward_auth directive.
    /// </summary>
    /// <returns>200 OK if authorized, 401/403 if not authorized.</returns>
    [HttpGet( "authorize" )]
    [Authorize]
    public async Task<IActionResult> Authorize( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        
        if (user == null) {
            _logger.LogWarning( "Dashboard authorization failed: User not found" );
            return Unauthorized( new { error = "User not authenticated" } );
        }

        bool hasAccess = await _userManager.IsInRoleAsync( user, Roles.AspireDashboardAccess );
        
        if (!hasAccess) {
            _logger.LogWarning(
                "Dashboard authorization denied for user {UserId} - missing {Role} role",
                user.Id,
                Roles.AspireDashboardAccess
            );
            return StatusCode( 403, new { error = "Access denied: AspireDashboardAccess role required" } );
        }

        _logger.LogInformation( "Dashboard authorization granted for user {UserId}", user.Id );
        return Ok( new { authorized = true } );
    }
}
