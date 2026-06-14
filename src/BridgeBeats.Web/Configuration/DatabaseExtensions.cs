using BridgeBeats.Contracts.Constants;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Web.Configuration;

/// <summary>
/// Extension methods for database initialization.
/// </summary>
public static class DatabaseExtensions {
    /// <summary>
    /// Ensures the database is created and applies any pending migrations.
    /// Seeds required roles if they don't exist.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for method chaining.</returns>
    public static async Task<WebApplication> InitializeDatabaseAsync( this WebApplication app ) {
        using IServiceScope scope = app.Services.CreateScope( );
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>( );

        // Apply pending migrations and create the database if it doesn't exist
        await context.Database.MigrateAsync( );

        // Recovery pass: clear any rate-limit windows that have already expired so users
        // pinned by the pre-fix non-persisted-reset bug are released on startup. Idempotent —
        // a no-op once windows are fresh, safe to run on every boot.
        DateTime windowFloor = DateTime.UtcNow.AddHours( -1 );
        _ = await context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE AspNetUsers
               SET RequestCount = 0,
                   RateLimitWindowStart = NULL
               WHERE RateLimitWindowStart IS NOT NULL
                 AND RateLimitWindowStart <= {windowFloor}" );

        // Seed the AspireDashboardAccess role if it doesn't exist
        await SeedRolesAsync( roleManager );

        return app;
    }

    /// <summary>
    /// Seeds required application roles.
    /// </summary>
    /// <param name="roleManager">The role manager.</param>
    private static async Task SeedRolesAsync( RoleManager<IdentityRole> roleManager ) {
        // Create AspireDashboardAccess role if it doesn't exist
        if (!await roleManager.RoleExistsAsync( Roles.AspireDashboardAccess )) {
            _ = await roleManager.CreateAsync( new IdentityRole( Roles.AspireDashboardAccess ) );
        }
    }
}
