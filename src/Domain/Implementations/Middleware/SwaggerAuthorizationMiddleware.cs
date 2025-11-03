using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace TuneBridge.Domain.Implementations.Middleware;

/// <summary>
/// Middleware to restrict access to Swagger UI to authenticated users only.
/// </summary>
public class SwaggerAuthorizationMiddleware {
    private readonly RequestDelegate _next;

    public SwaggerAuthorizationMiddleware( RequestDelegate next ) {
        _next = next;
    }

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

        await _next( context );
    }
}
