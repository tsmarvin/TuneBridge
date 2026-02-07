using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Cache;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Extensions {

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
    }
}
