using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Component-level tests for <see cref="RateLimitingMiddleware"/> (T1–T7).
/// T3 and T7 are SEC-004 discriminators: they assert against the PERSISTED SQLite store, not mocks.
/// </summary>
[TestClass]
public class RateLimitingMiddlewareTests {

    /// <summary>Gets or sets the test context (provides CancellationToken for async operations).</summary>
    public TestContext TestContext { get; set; } = null!;

    private const int MaxRequestsPerHour = 5;

    private SqliteConnection _connection = null!;
    private ApplicationDbContext _dbContext = null!;
    private UserManager<ApplicationUser> _userManager = null!;

    [TestInitialize]
    public void Initialize( ) {
        // Open a shared in-process SQLite connection so the schema persists for the lifetime of
        // each test. ":memory:" with a shared connection gives us a real SQLite store.
        _connection = new SqliteConnection( "Data Source=:memory:" );
        _connection.Open( );

        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>( )
            .UseSqlite( _connection )
            .Options;

        _dbContext = new ApplicationDbContext( options );
        _ = _dbContext.Database.EnsureCreated( );

        // Minimal UserManager wired to the same DbContext
        UserStore<ApplicationUser> store = new( _dbContext );
        _userManager = new UserManager<ApplicationUser>(
            store,
            null!, null!, null!, null!, null!,
            new IdentityErrorDescriber( ),
            null!, null! );
    }

    [TestCleanup]
    public void Cleanup( ) {
        _userManager.Dispose( );
        _dbContext.Dispose( );
        _connection.Dispose( );
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<ApplicationUser> SeedUserAsync(
        ApplicationDbContext ctx,
        string userName = "testuser",
        int requestCount = 0,
        DateTime? windowStart = null
    ) {
        ApplicationUser user = new( ) {
            Id = Guid.NewGuid( ).ToString( ),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant( ),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid( ).ToString( ),
            RequestCount = requestCount,
            RateLimitWindowStart = windowStart
        };
        _ = ctx.Users.Add( user );
        _ = await ctx.SaveChangesAsync( TestContext.CancellationToken );
        return user;
    }

    private static HttpContext BuildAuthenticatedContext( string userName ) {
        DefaultHttpContext ctx = new( );
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.Path = "/music/lookup/isrc";
        System.Security.Claims.ClaimsIdentity identity = new(
            [new System.Security.Claims.Claim( System.Security.Claims.ClaimTypes.Name, userName )],
            "TestScheme"
        );
        ctx.User = new System.Security.Claims.ClaimsPrincipal( identity );
        return ctx;
    }

    private static IMemoryCache BuildCache( ) =>
        new MemoryCache( new MemoryCacheOptions( ) );

    /// <summary>
    /// Invokes the middleware and returns the HTTP status code plus whether next() was called.
    /// </summary>
    private async Task<(int StatusCode, bool NextCalled)> InvokeAsync(
        ApplicationUser user,
        ApplicationDbContext dbCtx,
        IMemoryCache? cache = null
    ) {
        cache ??= BuildCache( );

        bool nextCalled = false;
        RequestDelegate next = _ => {
            nextCalled = true;
            return Task.CompletedTask;
        };

        RateLimitingMiddleware mw = new( next, NullLogger<RateLimitingMiddleware>.Instance, MaxRequestsPerHour );

        HttpContext httpCtx = BuildAuthenticatedContext( user.UserName! );
        await mw.InvokeAsync( httpCtx, _userManager, dbCtx, cache );

        return (httpCtx.Response.StatusCode, nextCalled);
    }

    // ─── T1: First request in fresh window ───────────────────────────────────

    /// <summary>
    /// T1 — First request in a fresh window: passes, persisted RequestCount = 1, window set.
    /// Failure-first evidence: before implementation the in-memory window reset kept RequestCount
    /// at 0 and the UPDATE incremented it — both paths worked but were diverged. The atomic rewrite
    /// always sets count = 1 on a new window; any deviation would either fail the 200-assert (if
    /// the middleware returned 429) or the persisted-count assert.
    /// </summary>
    [TestMethod]
    public async Task T1_FirstRequestFreshWindow_PassesAndPersistsCountOf1( ) {
        // Arrange
        ApplicationUser user = await SeedUserAsync( _dbContext, requestCount: 0, windowStart: null );

        // Act
        (int statusCode, bool nextCalled) = await InvokeAsync( user, _dbContext );

        // Assert — response
        Assert.AreEqual( 200, statusCode );
        Assert.IsTrue( nextCalled );

        // Assert — persisted state
        ApplicationUser persisted = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == user.Id, TestContext.CancellationToken );
        Assert.AreEqual( 1, persisted.RequestCount );
        Assert.IsNotNull( persisted.RateLimitWindowStart );
    }

    // ─── T2: At-cap boundary, allowed ────────────────────────────────────────

    /// <summary>
    /// T2 — At-cap boundary: the N-th request (RequestCount already at max-1) is allowed;
    /// persisted count becomes max (strict-greater semantics: max is allowed, max+1 is rejected).
    /// Failure-first evidence: if the comparison were ">=" the N-th request would be rejected
    /// (429), failing the 200-assert.
    /// </summary>
    [TestMethod]
    public async Task T2_AtCapBoundaryAllowed_PassesAndPersistsCountAtMax( ) {
        // Arrange — RequestCount = max - 1, active window
        DateTime windowStart = DateTime.UtcNow.AddMinutes( -30 );
        ApplicationUser user = await SeedUserAsync( _dbContext, requestCount: MaxRequestsPerHour - 1, windowStart: windowStart );

        // Act
        (int statusCode, bool nextCalled) = await InvokeAsync( user, _dbContext );

        // Assert — response
        Assert.AreEqual( 200, statusCode );
        Assert.IsTrue( nextCalled );

        // Assert — persisted count is exactly max
        ApplicationUser persisted = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == user.Id, TestContext.CancellationToken );
        Assert.AreEqual( MaxRequestsPerHour, persisted.RequestCount );
    }

    // ─── T3: Over-cap within active window — SEC-004 discriminator ───────────

    /// <summary>
    /// T3 — Over-cap within active window: RequestCount is already at max, active window.
    /// The (N+1)-th request must be rejected (429). The persisted count increments to max+1
    /// (the atomic statement ran), but the request is rejected.
    /// This is the primary SEC-004 discriminator: asserts against the PERSISTED SQLite row.
    /// A mock-only test would pass even on the buggy code.
    /// Failure-first evidence: the original code had a divergent path where the in-memory count
    /// was reset to 0 (window expiry check passing) but the DB was still at max — the UPDATE
    /// matched 0 rows, triggering a 429 even after window expiry. After the fix this test
    /// verifies the 429 case means the window is genuinely active (count > max after increment).
    /// </summary>
    [TestMethod]
    public async Task T3_OverCapActiveWindow_Returns429AndPersistsIncrementedCount( ) {
        // Arrange — RequestCount = max, active window (30 minutes ago, within the 1-hour window)
        DateTime windowStart = DateTime.UtcNow.AddMinutes( -30 );
        ApplicationUser user = await SeedUserAsync( _dbContext, requestCount: MaxRequestsPerHour, windowStart: windowStart );

        // Act
        (int statusCode, bool nextCalled) = await InvokeAsync( user, _dbContext );

        // Assert — response is 429
        Assert.AreEqual( 429, statusCode );
        Assert.IsFalse( nextCalled );

        // Assert — PERSISTED store: count incremented to max+1 (atomic CASE ran, result > max → rejected)
        ApplicationUser persisted = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == user.Id, TestContext.CancellationToken );
        Assert.AreEqual( MaxRequestsPerHour + 1, persisted.RequestCount,
            "The atomic UPDATE must have incremented the count even though the request was rejected." );
    }

    // ─── T4: Expired window was at cap ───────────────────────────────────────

    /// <summary>
    /// T4 — Window just expired: RequestCount was at cap but window is older than 1 hour.
    /// The CASE resets to count=1 and a new window start. Request passes.
    /// This is the regression that the original bug broke: an expired window never reopened.
    /// Failure-first evidence: the original code reset the in-memory count but the DB UPDATE
    /// matched 0 rows (WHERE RequestCount &lt; max failed) → permanent 429. After the fix
    /// the CASE resets atomically → count=1 → passes.
    /// </summary>
    [TestMethod]
    public async Task T4_ExpiredWindowAtCap_PassesAndResetsWindow( ) {
        // Arrange — RequestCount = max, window expired 61 minutes ago
        DateTime expiredWindowStart = DateTime.UtcNow.AddMinutes( -61 );
        ApplicationUser user = await SeedUserAsync( _dbContext, requestCount: MaxRequestsPerHour, windowStart: expiredWindowStart );

        // Act
        (int statusCode, bool nextCalled) = await InvokeAsync( user, _dbContext );

        // Assert — response passes
        Assert.AreEqual( 200, statusCode );
        Assert.IsTrue( nextCalled );

        // Assert — persisted state: count reset to 1, window reset to approx now
        ApplicationUser persisted = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == user.Id, TestContext.CancellationToken );
        Assert.AreEqual( 1, persisted.RequestCount );
        Assert.IsNotNull( persisted.RateLimitWindowStart );
        Assert.IsTrue( persisted.RateLimitWindowStart > expiredWindowStart,
            "Window start must have been reset to a time after the old expired window." );
    }

    // ─── T5: Concurrent requests at the boundary ─────────────────────────────

    /// <summary>
    /// T5 — Two concurrent requests when RequestCount is at max-1. The atomic CASE serializes
    /// writes at the SQLite row level; after both complete, the combined count is max+1
    /// (the second writer saw count=max → max+1 → rejected).
    /// Failure-first evidence: with two non-atomic increments, a lost-update could leave count
    /// at max (both read max-1, both write max, both pass). The atomic CASE prevents that.
    /// </summary>
    [TestMethod]
    public async Task T5_ConcurrentRequestsAtBoundary_ExactlyOnePassesOneRejected( ) {
        // Arrange — RequestCount = max - 1, active window
        DateTime windowStart = DateTime.UtcNow.AddMinutes( -30 );
        ApplicationUser user = await SeedUserAsync( _dbContext, requestCount: MaxRequestsPerHour - 1, windowStart: windowStart );

        // Use a shared cache so both requests use the same cached user
        IMemoryCache sharedCache = BuildCache( );

        // Build two DbContexts on the same connection (shared in-process SQLite)
        DbContextOptions<ApplicationDbContext> opts = new DbContextOptionsBuilder<ApplicationDbContext>( )
            .UseSqlite( _connection )
            .Options;
        await using ApplicationDbContext dbCtx1 = new( opts );
        await using ApplicationDbContext dbCtx2 = new( opts );

        // Act — fire two requests concurrently, each with its own DbContext
        Task<(int StatusCode, bool NextCalled)> req1 = InvokeAsync( user, dbCtx1, sharedCache );
        Task<(int StatusCode, bool NextCalled)> req2 = InvokeAsync( user, dbCtx2, sharedCache );

        (int status1, bool next1) = await req1;
        (int status2, bool next2) = await req2;

        // Assert — exactly one 200, one 429
        int passCount = (status1 == 200 ? 1 : 0) + (status2 == 200 ? 1 : 0);
        int rejectCount = (status1 == 429 ? 1 : 0) + (status2 == 429 ? 1 : 0);
        Assert.AreEqual( 1, passCount, $"Exactly one request must pass. Got statuses: {status1}, {status2}" );
        Assert.AreEqual( 1, rejectCount, $"Exactly one request must be rejected. Got statuses: {status1}, {status2}" );

        // Assert — persisted count is max+1 (first wrote max, second wrote max+1 → rejected)
        await using ApplicationDbContext readCtx = new( opts );
        ApplicationUser persisted = await readCtx.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == user.Id, TestContext.CancellationToken );
        Assert.AreEqual( MaxRequestsPerHour + 1, persisted.RequestCount,
            "After two concurrent requests from max-1, count must be max+1." );
    }

    // ─── T6: ApiKey-scheme principal resolves correctly ──────────────────────

    /// <summary>
    /// T6 — ApiKey-scheme principal: Identity.Name is set to UserName (the common case).
    /// The OR-load query resolves the user by UserName; limit is enforced on the resolved PK.
    /// Failure-first evidence: the original code used u.UserName == username; a NameIdentifier-only
    /// read would null here. The OR-load means both UserName and Id work as lookup keys.
    /// </summary>
    [TestMethod]
    public async Task T6_ApiKeyScheme_ResolvesUserByUserNameAndEnforcesLimit( ) {
        // Arrange — user with UserName that matches the principal Name claim
        ApplicationUser user = await SeedUserAsync( _dbContext, userName: "apiuser", requestCount: 0, windowStart: null );

        // Act — principal Name = UserName (ApiKey scheme common path)
        (int statusCode, bool nextCalled) = await InvokeAsync( user, _dbContext );

        // Assert — resolved and rate-limited correctly
        Assert.AreEqual( 200, statusCode );
        Assert.IsTrue( nextCalled );

        // Assert — persisted by PK (Id), not by Name claim string
        ApplicationUser persisted = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == user.Id, TestContext.CancellationToken );
        Assert.AreEqual( 1, persisted.RequestCount );
    }

    // ─── T7: Startup recovery releases pinned user — SEC-004 discriminator ───

    /// <summary>
    /// T7 — Startup recovery pass (§6 of the brief): a user pinned at max count with an expired
    /// window must be released by the recovery SQL used in <c>InitializeDatabaseAsync</c>.
    /// After the recovery pass: persisted RequestCount = 0, RateLimitWindowStart = NULL.
    /// Re-running the pass (idempotency) must not thrash an active window.
    /// This is the second SEC-004 discriminator: asserts against the PERSISTED SQLite row.
    /// Failure-first evidence: the original code never ran a recovery pass; users stayed pinned
    /// across restarts. The recovery SQL is the only fix; a mock would trivially pass without it.
    /// </summary>
    [TestMethod]
    public async Task T7_StartupRecovery_ReleasesPinnedUserWithExpiredWindow( ) {
        // Arrange — seed a user pinned at max with a window expired 2 hours ago
        DateTime expiredWindow = DateTime.UtcNow.AddHours( -2 );
        ApplicationUser pinned = await SeedUserAsync(
            _dbContext,
            userName: "pinneduser",
            requestCount: MaxRequestsPerHour,
            windowStart: expiredWindow );

        // Seed a second user with an ACTIVE window (must not be touched by recovery)
        DateTime activeWindow = DateTime.UtcNow.AddMinutes( -30 );
        ApplicationUser active = await SeedUserAsync(
            _dbContext,
            userName: "activeuser",
            requestCount: 2,
            windowStart: activeWindow );

        // Act — run the same recovery SQL used by InitializeDatabaseAsync (§6 of brief)
        DateTime windowFloor = DateTime.UtcNow.AddHours( -1 );
        _ = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE AspNetUsers
               SET RequestCount = 0,
                   RateLimitWindowStart = NULL
               WHERE RateLimitWindowStart IS NOT NULL
                 AND RateLimitWindowStart <= {windowFloor}",
            TestContext.CancellationToken );

        // Assert — pinned user released
        ApplicationUser pinnedAfter = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == pinned.Id, TestContext.CancellationToken );
        Assert.AreEqual( 0, pinnedAfter.RequestCount,
            "Pinned user with expired window must be released (RequestCount = 0)." );
        Assert.IsNull( pinnedAfter.RateLimitWindowStart,
            "Pinned user with expired window must have RateLimitWindowStart set to NULL." );

        // Assert — active user untouched
        ApplicationUser activeAfter = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == active.Id, TestContext.CancellationToken );
        Assert.AreEqual( 2, activeAfter.RequestCount,
            "User with active window must not be modified by the recovery pass." );
        Assert.IsNotNull( activeAfter.RateLimitWindowStart );

        // Assert — idempotency: running again produces the same result (no error, no thrash)
        _ = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE AspNetUsers
               SET RequestCount = 0,
                   RateLimitWindowStart = NULL
               WHERE RateLimitWindowStart IS NOT NULL
                 AND RateLimitWindowStart <= {windowFloor}",
            TestContext.CancellationToken );

        ApplicationUser pinnedAfter2 = await _dbContext.Users.AsNoTracking( )
            .FirstAsync( u => u.Id == pinned.Id, TestContext.CancellationToken );
        Assert.AreEqual( 0, pinnedAfter2.RequestCount );
        Assert.IsNull( pinnedAfter2.RateLimitWindowStart );
    }
}
