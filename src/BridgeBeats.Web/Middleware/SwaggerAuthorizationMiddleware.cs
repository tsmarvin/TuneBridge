namespace BridgeBeats.Web.Middleware;

/// <summary>
/// Gates the Swagger UI behind authentication, redirecting unauthenticated callers to the login page.
/// </summary>
/// <remarks>
/// Requests under <c>/swagger</c> require an authenticated user, with the single exception of the
/// OpenAPI document at <c>/swagger/v1/swagger.json</c>, which remains publicly reachable. All other
/// paths pass through untouched.
/// </remarks>
/// <param name="next">The next delegate in the request pipeline.</param>
public class SwaggerAuthorizationMiddleware( RequestDelegate next ) {
    /// <summary>
    /// Redirects unauthenticated callers of the Swagger UI to the login page; otherwise forwards the
    /// request to the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request has been forwarded or redirected.</returns>
    public async Task InvokeAsync( HttpContext context ) {
        // Allow access to swagger.json even without authentication (needed for UI to work)
        // but require authentication for the UI itself
        if (
            context.Request.Path.StartsWithSegments( "/swagger" ) &&
            !string.Equals( context.Request.Path.Value, "/swagger/v1/swagger.json", System.StringComparison.OrdinalIgnoreCase ) &&
            context.User?.Identity?.IsAuthenticated != true
        ) {
            // Redirect to login page
            context.Response.Redirect( "/account/login" );
            return;
        }

        await next( context );
    }
}
