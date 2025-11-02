using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

    public async Task InvokeAsync( HttpContext context, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext ) {
        // Skip rate limiting for the public /music/lookup/url endpoint (but not /music/lookup/urlList)
        string path = context.Request.Path.Value ?? string.Empty;
        if (path.Equals( "/music/lookup/url", StringComparison.OrdinalIgnoreCase )) {
            await _next( context );
            return;
        }

        // Skip rate limiting for unauthenticated users (they'll be rejected by [Authorize])
        if (!context.User.Identity?.IsAuthenticated ?? true) {
            await _next( context );
            return;
        }

        // Get the username from the authenticated user
        string? username = context.User.Identity?.Name;
        if (string.IsNullOrEmpty( username )) {
            await _next( context );
            return;
        }

        // Get the user from database by username with tracking enabled for updates
        ApplicationUser? user = await userManager.Users
            .FirstOrDefaultAsync( u => u.UserName == username );

        if (user == null) {
            await _next( context );
            return;
        }

        DateTime now = DateTime.UtcNow;

        // Use a database transaction to ensure atomic updates and prevent race conditions
        using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync( );
        try {
            // Reload user with lock to prevent race conditions
            await dbContext.Entry( user ).ReloadAsync( );

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
                    "Rate limit exceeded for user {Username}. Window resets in {Minutes} minutes.",
                    username,
                    Math.Ceiling( timeRemaining.TotalMinutes )
                );

                await transaction.RollbackAsync( );

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers["Retry-After"] = ( (int)timeRemaining.TotalSeconds ).ToString( );
                await context.Response.WriteAsJsonAsync( new {
                    error = "Rate limit exceeded",
                    message = $"Maximum {MaxRequestsPerHour} requests per hour allowed. Please try again in {Math.Ceiling( timeRemaining.TotalMinutes )} minutes.",
                    retryAfter = (int)timeRemaining.TotalSeconds
                } );
                return;
            }

            // Increment request count atomically
            user.RequestCount++;
            _ = await userManager.UpdateAsync( user );
            await dbContext.SaveChangesAsync( );

            await transaction.CommitAsync( );
        } catch {
            await transaction.RollbackAsync( );
            throw;
        }

        await _next( context );
    }
}
