using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Cache;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Extensions {

    /// <summary>
    /// Dependency-injection registration helpers for the Redis-backed cache services
    /// in the Infrastructure layer (genre cache and media-link cache).
    /// </summary>
    public static class CacheServiceExtensions {

        /// <summary>
        /// Registers the Redis-backed genre cache as a singleton implementation of
        /// <see cref="BridgeBeats.Contracts.Interfaces.IGenreCacheService"/>.
        /// </summary>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
        /// <remarks>
        /// The implementation resolves the shared <c>IConnectionMultiplexer</c> and a typed
        /// logger from the container at activation time. Registered with singleton lifetime.
        /// </remarks>
        public static IServiceCollection AddGenreCache( this IServiceCollection services ) {
            _ = services.AddSingleton<IGenreCacheService>( s => new RedisGenreCache(
                s.GetRequiredService<IConnectionMultiplexer>( ),
                s.GetRequiredService<ILogger<RedisGenreCache>>( )
            ) );

            return services;
        }

        /// <summary>
        /// Registers the Redis-backed media-link cache as a singleton implementation of
        /// <see cref="BridgeBeats.Contracts.Interfaces.IMediaLinkCacheRepository"/>. Call this only
        /// when ATProto storage is configured, since the implementation resolves
        /// <see cref="BridgeBeats.Contracts.Interfaces.IATProtoStorageService"/> from the container.
        /// </summary>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <param name="cacheDays">
        /// The number of days a cached media-link entry remains valid before it is treated as stale.
        /// </param>
        /// <param name="atProtoUserDID">
        /// The ATProto user DID (decentralized identifier) of the service account whose PDS records
        /// the cache indexes and reads through.
        /// </param>
        /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
        /// <remarks>
        /// The implementation resolves the shared <c>IConnectionMultiplexer</c>, the ATProto storage
        /// service, and a typed logger from the container at activation time, and is supplied the
        /// <paramref name="cacheDays"/> expiry window and <paramref name="atProtoUserDID"/> as
        /// captured values. Registered with singleton lifetime.
        /// </remarks>
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
