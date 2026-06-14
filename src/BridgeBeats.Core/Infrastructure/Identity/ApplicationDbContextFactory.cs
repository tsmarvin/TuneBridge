using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Design-time factory that builds an <see cref="ApplicationDbContext"/> backed by SQLite,
/// used by EF Core tooling (for example, when creating or applying migrations).
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
    /// <summary>
    /// Creates an <see cref="ApplicationDbContext"/> configured to use the SQLite database
    /// <c>bridgebeats.db</c>.
    /// </summary>
    /// <param name="args">Command-line arguments supplied by the design-time tooling. Not used.</param>
    /// <returns>A configured <see cref="ApplicationDbContext"/> instance.</returns>
    public ApplicationDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<ApplicationDbContext> optionsBuilder = new();
        _ = optionsBuilder.UseSqlite( "Data Source=bridgebeats.db" );

        return new ApplicationDbContext( optionsBuilder.Options );
    }
}
