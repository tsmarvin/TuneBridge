using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Identity;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Infrastructure {

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
                _ = services.AddATProtoStorage( atProtoIdentifier, atProtoPassword );
                _ = services.AddRedisMediaLinkCache( cacheDays, atProtoUserDID! );
            }

            // Register genre cache (always available when Redis is configured)
            _ = services.AddGenreCache( );

            // Register queue infrastructure (shared services)
            _ = services.AddQueueInfrastructure( );

            return services;
        }

        /// <summary>
        /// Adds all BridgeBeats Infrastructure services including databases, identity, caching, and storage.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="identityConnectionString">Connection string for the identity database.</param>
        /// <param name="linkCacheConnectionString">Connection string for the link cache database (legacy, ignored when Redis is available).</param>
        /// <param name="cacheDays">Number of days to cache media link results.</param>
        /// <param name="atProtoIdentifier">Optional ATProto identifier for storage.</param>
        /// <param name="atProtoPassword">Optional ATProto password for storage.</param>
        /// <param name="atProtoUserDID">Optional ATProto user DID for storage.</param>
        /// <returns>The configured service collection.</returns>
        [Obsolete( "Use the overload without linkCacheConnectionString. Redis is now used for media link caching." )]
        public static IServiceCollection AddBridgeBeatsInfrastructure(
            this IServiceCollection services,
            string identityConnectionString,
            string linkCacheConnectionString,
            int cacheDays,
            string? atProtoIdentifier = null,
            string? atProtoPassword = null,
            string? atProtoUserDID = null
        ) {
            // linkCacheConnectionString is ignored - Redis is used instead
            return services.AddBridgeBeatsInfrastructure(
                identityConnectionString,
                cacheDays,
                atProtoIdentifier,
                atProtoPassword,
                atProtoUserDID
            );
        }
    }
}
