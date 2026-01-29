using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Extensions {

    /// <summary>
    /// Aggregate extension methods for registering all Infrastructure services.
    /// </summary>
    public static class InfrastructureServiceExtensions {

        /// <summary>
        /// Adds all BridgeBeats Infrastructure services including databases, identity, caching, and storage.
        /// Uses Redis for media link caching (connection must be registered via Aspire).
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="identityConnectionString">Connection string for the identity database.</param>
        /// <param name="cacheDays">Number of days to cache media link results.</param>
        /// <param name="atProtoIdentifier">Optional ATProto identifier for storage.</param>
        /// <param name="atProtoPassword">Optional ATProto password for storage.</param>
        /// <param name="atProtoUserDID">Optional ATProto user DID for storage.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddBridgeBeatsInfrastructure(
            this IServiceCollection services,
            string identityConnectionString,
            int cacheDays,
            string? atProtoIdentifier = null,
            string? atProtoPassword = null,
            string? atProtoUserDID = null
        ) {
            // Register database context factory for Identity only (SQLite)
            _ = services.AddDbContextFactory<ApplicationDbContext>( options =>
                options.UseSqlite(
                    identityConnectionString,
                    b => b.MigrationsAssembly( "BridgeBeats.Infrastructure" )
                )
            );

            // Register Identity services
            _ = services.AddBridgeBeatsIdentity( );

            // Register ATProto storage and Redis cache services only if configured
            bool atProtoConfigured = !string.IsNullOrWhiteSpace( atProtoIdentifier ) &&
                                     !string.IsNullOrWhiteSpace( atProtoPassword ) &&
                                     !string.IsNullOrWhiteSpace( atProtoUserDID );

            if (atProtoConfigured) {
                _ = services.AddATProtoSessionManager( atProtoIdentifier, atProtoPassword );
                _ = services.AddATProtoStorage( );
                _ = services.AddRedisMediaLinkCache( cacheDays, atProtoUserDID! );
            }

            // Register genre cache (always available when Redis is configured)
            _ = services.AddGenreCache( );

            // Register queue infrastructure (shared services)
            _ = services.AddQueueInfrastructure( );

            return services;
        }
    }
}
