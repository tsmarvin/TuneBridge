using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Extensions;

/// <summary>Registers the shared SQLite application database for Web, AppHost, and worker composition.</summary>
public static class ApplicationDatabaseServiceExtensions {
    /// <summary>
    /// Adds the protected application database context factory. Every runtime context receives the
    /// shared Data Protection token protector; no runtime registration can silently fall back to the
    /// design-time, unprotected context model.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="connectionString">The bootstrap-only SQLite connection string.</param>
    /// <returns>The supplied service collection.</returns>
    public static IServiceCollection AddBridgeBeatsApplicationDatabase(
        this IServiceCollection services,
        string connectionString
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( connectionString );

        _ = services.AddSingleton<IDbContextFactory<ApplicationDbContext>>( serviceProvider => {
            IDataProtector tokenProtector = serviceProvider
                .GetRequiredService<IDataProtectionProvider>( )
                .CreateProtector( ApplicationDbContext.TokenProtectorPurpose );

            DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>( )
                .UseSqlite(
                    connectionString,
                    sqlite => sqlite.MigrationsAssembly( "BridgeBeats.Core" )
                )
                .Options;

            return new ProtectedApplicationDbContextFactory( options, tokenProtector );
        } );

        _ = services.AddScoped( serviceProvider => serviceProvider
            .GetRequiredService<IDbContextFactory<ApplicationDbContext>>( )
            .CreateDbContext( ) );

        return services;
    }

    private sealed class ProtectedApplicationDbContextFactory(
        DbContextOptions<ApplicationDbContext> options,
        IDataProtector tokenProtector
    ) : IDbContextFactory<ApplicationDbContext> {
        public ApplicationDbContext CreateDbContext( ) => new( options, tokenProtector );
    }
}
