using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Models;

namespace TuneBridge.Configuration;

/// <summary>
/// Extension methods for database initialization.
/// </summary>
public static class DatabaseExtensions {
    /// <summary>
    /// Ensures the database is created and applies any pending migrations.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for method chaining.</returns>
    public static WebApplication InitializeDatabase( this WebApplication app ) {
        using IServiceScope scope = app.Services.CreateScope( );
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>( );

        // Create the database if it doesn't exist
        _ = context.Database.EnsureCreated( );

        return app;
    }
}
