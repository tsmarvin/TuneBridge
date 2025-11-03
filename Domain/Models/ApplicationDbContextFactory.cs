using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TuneBridge.Domain.Models;

/// <summary>
/// Factory for creating ApplicationDbContext instances at design time (for migrations).
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext> {
    public ApplicationDbContext CreateDbContext( string[] args ) {
        DbContextOptionsBuilder<ApplicationDbContext> optionsBuilder = new();
        _ = optionsBuilder.UseSqlite( "Data Source=tunebridge.db" );

        return new ApplicationDbContext( optionsBuilder.Options );
    }
}
