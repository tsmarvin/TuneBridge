using BridgeBeats.Contracts.Constants;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for Aspire Dashboard authorization checks.
/// </summary>
[ApiController]
[Route( "api/[controller]" )]
public partial class DashboardController : ControllerBase {
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
            LogAuthorizationFailedUserNotFound( _logger );
            return Unauthorized( new { error = "User not authenticated" } );
        }

        bool hasAccess = await _userManager.IsInRoleAsync( user, Roles.AspireDashboardAccess );

        if (!hasAccess) {
            LogAuthorizationDeniedMissingRole( _logger, user.Id, Roles.AspireDashboardAccess );
            return StatusCode( 403, new { error = "Access denied: AspireDashboardAccess role required" } );
        }

        LogAuthorizationGranted( _logger, user.Id );
        return Ok( new { authorized = true } );
    }

    /// <summary>
    /// Logs that dashboard authorization failed because the user was not found.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Controllers.DashboardControllerAuthorizationFailedUserNotFound,
        Level = LogLevel.Warning,
        Message = "Dashboard authorization failed: User not found" )]
    private static partial void LogAuthorizationFailedUserNotFound( ILogger logger );

    /// <summary>
    /// Logs that dashboard authorization was denied due to a missing role.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Controllers.DashboardControllerAuthorizationDeniedMissingRole,
        Level = LogLevel.Warning,
        Message = "Dashboard authorization denied for user {UserId} - missing {Role} role" )]
    private static partial void LogAuthorizationDeniedMissingRole( ILogger logger, string userId, string role );

    /// <summary>
    /// Logs that dashboard authorization was granted.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Controllers.DashboardControllerAuthorizationGranted,
        Level = LogLevel.Information,
        Message = "Dashboard authorization granted for user {UserId}" )]
    private static partial void LogAuthorizationGranted( ILogger logger, string userId );
}
