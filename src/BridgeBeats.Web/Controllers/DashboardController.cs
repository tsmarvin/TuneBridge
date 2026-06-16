using BridgeBeats.Contracts.Constants;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Web API controller that gates access to the Aspire dashboard. Rooted at <c>api/dashboard</c>, it checks
/// that the authenticated user holds the required role before access is granted.
/// </summary>
/// <param name="userManager">Identity user manager used to resolve the current user and check role membership.</param>
/// <param name="logger">Logger for dashboard authorization decisions.</param>
[ApiController]
[Route( "api/[controller]" )]
public partial class DashboardController(
    UserManager<ApplicationUser> userManager,
    ILogger<DashboardController> logger
) : ControllerBase {

    /// <summary>
    /// Authorizes the current user for Aspire dashboard access by verifying the
    /// <see cref="BridgeBeats.Contracts.Constants.Roles.AspireDashboardAccess"/> role. This endpoint is used by
    /// Caddy's forward_auth directive.
    /// </summary>
    /// <returns>
    /// HTTP GET <c>api/dashboard/authorize</c>. <c>200 OK</c> with <c>authorized = true</c> when the user holds
    /// the required role; <c>401 Unauthorized</c> when no user is resolved; <c>403 Forbidden</c> when the role
    /// is missing. Requires an authenticated session via <c>[Authorize]</c>.
    /// </returns>
    [HttpGet( "authorize" )]
    [Authorize]
    public async Task<IActionResult> Authorize( ) {
        ApplicationUser? user = await userManager.GetUserAsync( User );

        if (user == null) {
            LogAuthorizationFailedUserNotFound( logger );
            return Unauthorized( new { error = "User not authenticated" } );
        }

        bool hasAccess = await userManager.IsInRoleAsync( user, Roles.AspireDashboardAccess );

        if (!hasAccess) {
            LogAuthorizationDeniedMissingRole( logger, user.Id, Roles.AspireDashboardAccess );
            return StatusCode( 403, new { error = "Access denied: AspireDashboardAccess role required" } );
        }

        LogAuthorizationGranted( logger, user.Id );
        return Ok( new { authorized = true } );
    }

    /// <summary>
    /// Logs that dashboard authorization failed because no user could be resolved.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    [LoggerMessage(
        EventId = LogEventIds.Controllers.DashboardControllerAuthorizationFailedUserNotFound,
        Level = LogLevel.Warning,
        Message = "Dashboard authorization failed: User not found" )]
    private static partial void LogAuthorizationFailedUserNotFound( ILogger logger );

    /// <summary>
    /// Logs that dashboard authorization was denied because the user lacks the required role.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="userId">The id of the user that was denied access.</param>
    /// <param name="role">The role that was required but missing.</param>
    [LoggerMessage(
        EventId = LogEventIds.Controllers.DashboardControllerAuthorizationDeniedMissingRole,
        Level = LogLevel.Warning,
        Message = "Dashboard authorization denied for user {UserId} - missing {Role} role" )]
    private static partial void LogAuthorizationDeniedMissingRole( ILogger logger, string userId, string role );

    /// <summary>
    /// Logs that dashboard authorization was granted.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="userId">The id of the authorized user.</param>
    [LoggerMessage(
        EventId = LogEventIds.Controllers.DashboardControllerAuthorizationGranted,
        Level = LogLevel.Information,
        Message = "Dashboard authorization granted for user {UserId}" )]
    private static partial void LogAuthorizationGranted( ILogger logger, string userId );
}
