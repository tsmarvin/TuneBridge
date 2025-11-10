using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TuneBridge.Domain.Models;

namespace TuneBridge.Domain.Implementations.Middleware;

/// <summary>
/// Middleware that enforces rate limiting on protected endpoints.
/// Uses in-memory caching to reduce database load.
/// </summary>
public class RateLimitingMiddleware {
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly int _maxRequestsPerHour;

    // Only these endpoints are rate-limited (all are POST). Public URL streaming endpoint is excluded.
    private static readonly HashSet<string> s_rateLimitedRoutes = new(
        [
            "/music/lookup/isrc",
            "/music/lookup/upc",
            "/music/lookup/title",
            "/music/lookup/urllist" // case-insensitive compare below
        ],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger for rate limiting events.</param>
    /// <param name="maxRequestsPerHour">Maximum number of requests allowed per hour per user.</param>
    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        int maxRequestsPerHour
    ) {
        _next = next;
        _logger = logger;
        _maxRequestsPerHour = maxRequestsPerHour;
    }

    /// <summary>
    /// Invokes the rate limiting middleware to check if the user has exceeded their hourly limit.
    /// </summary>
    /// <param name="context">The HTTP context for the current request.</param>
    /// <param name="userManager">User manager for retrieving user information.</param>
    /// <param name="dbContext">Database context for tracking request counts.</param>
    /// <param name="cache">Memory cache for storing rate limit data.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InvokeAsync( HttpContext context, UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext, IMemoryCache cache ) {
        string path = context.Request.Path.Value ?? string.Empty;

        // Only enforce rate limiting on specific protected search endpoints and only for POST requests
        if (!HttpMethods.IsPost( context.Request.Method ) || !s_rateLimitedRoutes.Contains( path )) {
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

        // Try to get user from cache first, then database
        string cacheKey = $"RateLimit_{username}";
        ApplicationUser? user = await cache.GetOrCreateAsync( cacheKey, async entry => {
            entry.SlidingExpiration = TimeSpan.FromMinutes( 5 ); // Cache for 5 minutes with sliding window
            return await userManager.Users.FirstOrDefaultAsync( u => u.UserName == username );
        } );

        if (user == null) {
            await _next( context );
            return;
        }

        DateTime now = DateTime.UtcNow;

        // Initialize or reset rate limit window if needed
        if (user.RateLimitWindowStart == null ||
            (now - user.RateLimitWindowStart.Value).TotalHours >= 1
        ) {
            user.RateLimitWindowStart = now;
            user.RequestCount = 0;
        }

        // Check if rate limit is exceeded
        if (user.RequestCount >= _maxRequestsPerHour) {
            TimeSpan timeRemaining = user.RateLimitWindowStart.Value.AddHours(1) - now;
            _logger.LogWarning(
                "Rate limit exceeded for user {Username}. Window resets in {Minutes} minutes.",
                username,
                Math.Ceiling( timeRemaining.TotalMinutes )
            );

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)timeRemaining.TotalSeconds).ToString( );
            await context.Response.WriteAsJsonAsync( new {
                error = "Rate limit exceeded",
                message = $"Maximum {_maxRequestsPerHour} requests per hour allowed. Please try again in {Math.Ceiling( timeRemaining.TotalMinutes )} minutes.",
                retryAfter = (int)timeRemaining.TotalSeconds
            } );
            return;
        }

        // Increment request count atomically using raw SQL to prevent race conditions
        int rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
 $@"UPDATE AspNetUsers
 SET RequestCount = RequestCount + 1
 WHERE Id = {user.Id} AND RequestCount < {_maxRequestsPerHour}"
 );

        // Invalidate cache to ensure fresh data on next request
        cache.Remove( cacheKey );

        // If no rows were updated, the rate limit was exceeded by a concurrent request
        if (rowsAffected == 0) {
            TimeSpan timeRemaining = user.RateLimitWindowStart.Value.AddHours(1) - now;
            _logger.LogWarning(
            "Rate limit exceeded for user {Username} (concurrent check). Window resets in {Minutes} minutes.",
            username,
            Math.Ceiling( timeRemaining.TotalMinutes )
            );

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)timeRemaining.TotalSeconds).ToString( );
            await context.Response.WriteAsJsonAsync( new {
                error = "Rate limit exceeded",
                message = $"Maximum {_maxRequestsPerHour} requests per hour allowed. Please try again in {Math.Ceiling( timeRemaining.TotalMinutes )} minutes.",
                retryAfter = (int)timeRemaining.TotalSeconds
            } );
            return;
        }

        await _next( context );
    }
}
