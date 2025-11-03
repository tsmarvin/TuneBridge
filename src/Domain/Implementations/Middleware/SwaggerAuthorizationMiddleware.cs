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
        // Check if the request is for Swagger UI or the Swagger JSON endpoint
        if (context.Request.Path.StartsWithSegments( "/swagger" )) {
            // Allow access to swagger.json even without authentication (needed for UI to work)
            // but require authentication for the UI itself
            if (!context.Request.Path.Value?.EndsWith( ".json" ) == true) {
                // Check if user is authenticated via cookie
                if (context.User?.Identity?.IsAuthenticated != true) {
                    // Redirect to login page
                    context.Response.Redirect( "/account/login" );
                    return;
                }
            }
        }

        await _next( context );
    }
}
