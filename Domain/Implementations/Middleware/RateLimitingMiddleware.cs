using Microsoft.AspNetCore.Identity;
using TuneBridge.Domain.Models;

namespace TuneBridge.Domain.Implementations.Middleware;

/// <summary>
/// Middleware that enforces rate limiting (20 requests per hour per user) on protected endpoints.
/// </summary>
public class RateLimitingMiddleware {
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private const int MaxRequestsPerHour = 20;

    public RateLimitingMiddleware( RequestDelegate next, ILogger<RateLimitingMiddleware> logger ) {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync( HttpContext context, UserManager<ApplicationUser> userManager ) {
        // Skip rate limiting for the public /music/lookup/url endpoint
        if (context.Request.Path.StartsWithSegments( "/music/lookup/url" ) &&
            !context.Request.Path.StartsWithSegments( "/music/lookup/urlList" )) {
            await _next( context );
            return;
        }

        // Skip rate limiting for unauthenticated users (they'll be rejected by [Authorize])
        if (!context.User.Identity?.IsAuthenticated ?? true) {
            await _next( context );
            return;
        }

        // Get the current user
        ApplicationUser? user = await userManager.GetUserAsync( context.User );
        if (user == null) {
            await _next( context );
            return;
        }

        DateTime now = DateTime.UtcNow;

        // Initialize or reset rate limit window if needed
        if (user.RateLimitWindowStart == null ||
            ( now - user.RateLimitWindowStart.Value ).TotalHours >= 1) {
            user.RateLimitWindowStart = now;
            user.RequestCount = 0;
        }

        // Check if rate limit is exceeded
        if (user.RequestCount >= MaxRequestsPerHour) {
            TimeSpan timeRemaining = user.RateLimitWindowStart.Value.AddHours( 1 ) - now;
            _logger.LogWarning(
                "Rate limit exceeded for user {UserId}. Window resets in {Minutes} minutes.",
                user.Id,
                Math.Ceiling( timeRemaining.TotalMinutes )
            );

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ( (int)timeRemaining.TotalSeconds ).ToString( );
            await context.Response.WriteAsJsonAsync( new {
                error = "Rate limit exceeded",
                message = $"Maximum {MaxRequestsPerHour} requests per hour allowed. Please try again in {Math.Ceiling( timeRemaining.TotalMinutes )} minutes.",
                retryAfter = (int)timeRemaining.TotalSeconds
            } );
            return;
        }

        // Increment request count
        user.RequestCount++;
        _ = await userManager.UpdateAsync( user );

        await _next( context );
    }
}
