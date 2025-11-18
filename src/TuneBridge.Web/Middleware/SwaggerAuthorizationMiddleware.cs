namespace TuneBridge.Web.Middleware;

/// <summary>
/// Middleware to restrict access to Swagger UI to authenticated users only.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="SwaggerAuthorizationMiddleware"/> class.
/// </remarks>
/// <param name="next">The next middleware in the pipeline.</param>
public class SwaggerAuthorizationMiddleware( RequestDelegate next ) {

    /// <summary>
    /// Invokes the middleware to check Swagger authorization.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
