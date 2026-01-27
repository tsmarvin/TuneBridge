using BridgeBeats.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Cache {

    /// <summary>
    /// Extension methods for registering cache and database services.
    /// </summary>
    public static class CacheServiceExtensions {

        /// <summary>
        /// Adds the Redis-based genre cache service.
        /// Redis connection must be available via <see cref="IConnectionMultiplexer"/>.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddGenreCache( this IServiceCollection services ) {
            _ = services.AddSingleton<IGenreCacheService>( s => new RedisGenreCache(
                s.GetRequiredService<IConnectionMultiplexer>( ),
                s.GetRequiredService<ILogger<RedisGenreCache>>( )
            ) );

            return services;
        }

        /// <summary>
        /// Adds the Redis-based media link cache repository.
        /// This method should only be called when ATProto storage is configured.
        /// Redis connection must be available via <see cref="IConnectionMultiplexer"/>.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="cacheDays">Number of days to cache media link results.</param>
        /// <param name="atProtoUserDID">ATProto user DID for storage (required).</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddRedisMediaLinkCache(
            this IServiceCollection services,
            int cacheDays,
            string atProtoUserDID
        ) {
            _ = services.AddSingleton<IMediaLinkCacheRepository>( s => new RedisMediaLinkCache(
                s.GetRequiredService<IConnectionMultiplexer>( ),
                s.GetRequiredService<IATProtoStorageService>( ),
                s.GetRequiredService<ILogger<RedisMediaLinkCache>>( ),
                cacheDays,
                atProtoUserDID
            ) );

            return services;
        }

        /// <summary>
        /// Adds the media link cache repository (backward compatibility wrapper).
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="cacheDays">Number of days to cache media link results.</param>
        /// <param name="atProtoUserDID">ATProto user DID for storage (required).</param>
        /// <returns>The configured service collection.</returns>
        [Obsolete( "Use AddRedisMediaLinkCache instead. This method will be removed in a future version." )]
        public static IServiceCollection AddMediaLinkCache(
            this IServiceCollection services,
            int cacheDays,
            string atProtoUserDID
        ) {
            return AddRedisMediaLinkCache( services, cacheDays, atProtoUserDID );
        }

        /// <summary>
        /// Adds the SQLite to Redis migration hosted service.
        /// This should be called when migrating from SQLite to Redis cache.
        /// The migrator will run once at startup and set a completion flag in Redis.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="sqliteConnectionString">SQLite connection string for the legacy cache database.</param>
        /// <param name="cacheDays">Number of days to cache media link results.</param>
        /// <param name="atProtoUserDID">ATProto user DID for storage (required).</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddSqliteToRedisMigration(
            this IServiceCollection services,
            string? sqliteConnectionString,
            int cacheDays,
            string atProtoUserDID
        ) {
            // Only register migration services if connection string is provided
            if (string.IsNullOrWhiteSpace( sqliteConnectionString )) {
                return services;
            }

            // Register SQLite context factory
            _ = services.AddDbContextFactory<MediaLinkCacheDbContext>( options =>
                options.UseSqlite(
                    sqliteConnectionString,
                    b => b.MigrationsAssembly( "BridgeBeats.Infrastructure" )
                )
            );

            // Register the migration hosted service
            _ = services.AddHostedService( s => new SqliteToRedisMigrator(
                s.GetRequiredService<IDbContextFactory<MediaLinkCacheDbContext>>( ),
                s.GetRequiredService<IConnectionMultiplexer>( ),
                s.GetRequiredService<ILogger<SqliteToRedisMigrator>>( ),
                cacheDays
            ) );

            return services;
        }
    }
}
