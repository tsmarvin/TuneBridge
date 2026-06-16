using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Logging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BridgeBeats.Web.Middleware;

/// <summary>
/// Enforces a per-user hourly request quota on the rate-limited lookup routes, returning HTTP 429
/// once a user exceeds the configured ceiling within a rolling one-hour window.
/// </summary>
/// <remarks>
/// Only authenticated POST requests to the ISRC, UPC, and title lookup routes are counted; all other
/// requests pass through. The per-user counter and window start are tracked on the
/// <see cref="ApplicationUser"/> row and updated atomically with a single SQL statement so concurrent
/// requests cannot lose counts. The window resets when the stored start is null or older than one hour.
/// When the quota is exceeded the response carries a <c>Retry-After</c> header and a JSON error body.
/// </remarks>
/// <param name="next">The next delegate in the request pipeline.</param>
/// <param name="logger">Logger used to record rate-limit-exceeded events.</param>
/// <param name="maxRequestsPerHour">The maximum number of counted requests permitted per user per hour.</param>
public partial class RateLimitingMiddleware(
    RequestDelegate next,
    ILogger<RateLimitingMiddleware> logger,
    int maxRequestsPerHour
) {
    // Only these endpoints are rate-limited (all are POST). Both public URL endpoints
    // (/music/lookup/url and /music/lookup/urlList) are excluded; only identifier-based
    // and name-search lookups are throttled.
    private static readonly HashSet<string> s_rateLimitedRoutes = new(
        [
            "/music/lookup/isrc",
            "/music/lookup/upc",
            "/music/lookup/title"
        ],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Counts the request against the authenticated user's hourly quota and short-circuits with HTTP 429
    /// when the quota is exceeded; otherwise forwards the request to the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="userManager">Identity user manager used to resolve the caller by user name or id.</param>
    /// <param name="dbContext">Database context used to atomically update the user's request counter and window.</param>
    /// <param name="cache">Memory cache holding the resolved user for a short sliding window to avoid repeated lookups.</param>
    /// <returns>A task that completes when the request has been forwarded or rejected.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext dbContext,
        IMemoryCache cache
    ) {
        string path = context.Request.Path.Value ?? string.Empty;

        // Only enforce rate limiting on specific protected search endpoints and only for POST requests
        if (!HttpMethods.IsPost( context.Request.Method ) || !s_rateLimitedRoutes.Contains( path )) {
            await next( context );
            return;
        }

        // Skip rate limiting for unauthenticated users (they'll be rejected by [Authorize])
        if (!context.User.Identity?.IsAuthenticated ?? true) {
            await next( context );
            return;
        }

        // `identity` is context.User.Identity?.Name — UserName under both schemes,
        // or the user Id when the ApiKey OwnerName fell back to Id (UserName null).
        string? identity = context.User.Identity?.Name;
        if (string.IsNullOrEmpty( identity )) {
            await next( context );
            return;
        }

        // Try to get user from cache first, then database.
        // OR-load on UserName or Id so both cookie (Name=UserName) and ApiKey (Name=UserName or Id)
        // schemes resolve to the same row. All writes use the resolved PK (user.Id) exclusively.
        string cacheKey = $"RateLimit_{identity}";
        ApplicationUser? user = await cache.GetOrCreateAsync( cacheKey, async entry => {
            entry.SlidingExpiration = TimeSpan.FromMinutes( 5 );
            return await userManager.Users
                .FirstOrDefaultAsync( u => u.UserName == identity || u.Id == identity );
        } );

        if (user == null) {
            await next( context );
            return;
        }

        // Atomic reset-or-increment in a single statement. When the window has expired
        // (or was never started), the window restarts at `now` with count = 1. Otherwise
        // the count increments. Evaluated against the row's own persisted window start,
        // so concurrent requests serialize correctly at the row level.
        // Returns the post-write RequestCount and the effective window start.
        DateTime now = DateTime.UtcNow;
        DateTime windowFloor = now.AddHours( -1 );

        List<RateLimitWriteResult> result = await dbContext.Database
            .SqlQuery<RateLimitWriteResult>(
                $@"UPDATE AspNetUsers
                SET
                    RateLimitWindowStart = CASE
                        WHEN RateLimitWindowStart IS NULL
                          OR RateLimitWindowStart <= {windowFloor}
                        THEN {now}
                        ELSE RateLimitWindowStart
                    END,
                    RequestCount = CASE
                        WHEN RateLimitWindowStart IS NULL
                          OR RateLimitWindowStart <= {windowFloor}
                        THEN 1
                        ELSE RequestCount + 1
                    END
                WHERE Id = {user.Id}
                RETURNING RequestCount, RateLimitWindowStart" )
            .ToListAsync( );

        // Invalidate the cached user so the next request reloads fresh counter state.
        cache.Remove( cacheKey );

        // User deleted between cache load and write — treat as missing user and pass through.
        if (result.Count == 0) {
            await next( context );
            return;
        }

        RateLimitWriteResult written = result[0];

        if (written.RequestCount > maxRequestsPerHour) {
            // The increment pushed the user past the cap within the active window.
            // Reject this request. (The row already reflects the over-cap count; that is
            // harmless — the window still expires normally and the next window resets to 1.)
            TimeSpan timeRemaining = written.RateLimitWindowStart.AddHours( 1 ) - now;
            LogRateLimitExceeded(
                logger,
                identity,
                Math.Ceiling( timeRemaining.TotalMinutes )
            );

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)timeRemaining.TotalSeconds).ToString( );
            await context.Response.WriteAsJsonAsync( new {
                error = "Rate limit exceeded",
                message = $"Maximum {maxRequestsPerHour} requests per hour allowed. Please try again in {Math.Ceiling( timeRemaining.TotalMinutes )} minutes.",
                retryAfter = (int)timeRemaining.TotalSeconds
            } );
            return;
        }

        await next( context );
    }

    /// <summary>
    /// Logs a warning when a user exceeds the hourly request quota.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="username">The user name (or id) of the throttled caller.</param>
    /// <param name="minutes">The number of minutes until the user's window resets.</param>
    [LoggerMessage(
        EventId = LogEventIds.Middleware.RateLimitingMiddlewareRateLimitExceeded,
        Level = LogLevel.Warning,
        Message = "Rate limit exceeded for user {Username}. Window resets in {Minutes} minutes." )]
    private static partial void LogRateLimitExceeded( ILogger logger, string? username, double minutes );
}

/// <summary>
/// Projection of the values returned by the atomic rate-limit update: the user's current request
/// count and the start of the active window.
/// </summary>
/// <param name="RequestCount">The request count after the update was applied.</param>
/// <param name="RateLimitWindowStart">The UTC timestamp marking the start of the current rate-limit window.</param>
internal sealed record RateLimitWriteResult( int RequestCount, DateTime RateLimitWindowStart );
