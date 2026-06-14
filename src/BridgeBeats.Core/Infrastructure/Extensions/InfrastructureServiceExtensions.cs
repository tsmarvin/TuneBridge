using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Extensions {

    /// <summary>
    /// Composition-root registration helper that wires the full Infrastructure layer
    /// (identity, ATProto storage, caches, and the queue) into the service collection.
    /// </summary>
    public static class InfrastructureServiceExtensions {

        /// <summary>
        /// Registers the complete BridgeBeats Infrastructure layer: the SQLite-backed application
        /// database context factory, ASP.NET Core Identity, optional ATProto session management and
        /// storage with its media-link cache, the genre cache, and the queue infrastructure.
        /// </summary>
        /// <param name="services">The service collection to add the registrations to.</param>
        /// <param name="identityConnectionString">
        /// The SQLite connection string for the application identity/database context. Migrations are
        /// resolved from the <c>BridgeBeats.Core</c> assembly.
        /// </param>
        /// <param name="cacheDays">
        /// The number of days a cached media-link entry remains valid before it is treated as stale.
        /// </param>
        /// <param name="atProtoIdentifier">
        /// The ATProto service-account identifier (handle) used to authenticate session management.
        /// When null, empty, or whitespace, ATProto storage and its media-link cache are not registered.
        /// </param>
        /// <param name="atProtoPassword">
        /// The ATProto service-account password (app password) used to authenticate session management.
        /// When null, empty, or whitespace, ATProto storage and its media-link cache are not registered.
        /// </param>
        /// <param name="atProtoUserDID">
        /// The ATProto user DID whose PDS records the media-link cache indexes. Required (non-null) only
        /// when ATProto storage is configured.
        /// </param>
        /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
        /// <remarks>
        /// ATProto storage, its session manager, and the Redis media-link cache are registered only when
        /// <paramref name="atProtoIdentifier"/>, <paramref name="atProtoPassword"/>, and
        /// <paramref name="atProtoUserDID"/> are all supplied. The genre cache and queue infrastructure
        /// are always registered. Redis-backed caching relies on an <c>IConnectionMultiplexer</c>
        /// registered via Aspire.
        /// </remarks>
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
                    b => b.MigrationsAssembly( "BridgeBeats.Core" )
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
