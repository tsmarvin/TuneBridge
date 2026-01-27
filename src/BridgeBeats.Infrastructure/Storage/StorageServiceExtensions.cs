using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Infrastructure.Storage {

    /// <summary>
    /// Extension methods for registering ATProto storage services.
    /// </summary>
    public static class StorageServiceExtensions {

        /// <summary>
        /// Adds the Redis-backed ATProto session manager.
        /// Must be called before AddATProtoStorage() and requires Redis connection.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="atProtoIdentifier">ATProto identifier (handle or DID).</param>
        /// <param name="atProtoPassword">ATProto app password.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddATProtoSessionManager(
            this IServiceCollection services,
            string? atProtoIdentifier,
            string? atProtoPassword
        ) {
            if (string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
                string.IsNullOrWhiteSpace( atProtoPassword )) {
                return services;
            }

            _ = services.AddSingleton<IATProtoSessionManager>( sp =>
                new RedisATProtoSessionManager(
                    sp.GetRequiredService<IConnectionMultiplexer>( ),
                    sp.GetRequiredService<ILogger<RedisATProtoSessionManager>>( ),
                    atProtoIdentifier,
                    atProtoPassword
                )
            );

            return services;
        }

        /// <summary>
        /// Adds ATProto storage services if credentials are provided.
        /// Requires <see cref="IATProtoSessionManager"/> to be registered first via <see cref="AddATProtoSessionManager"/>.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddATProtoStorage( this IServiceCollection services ) {
            _ = services.AddSingleton<IATProtoStorageService>( sp =>
                new ATProtoStorageService(
                    sp.GetRequiredService<IATProtoSessionManager>( ),
                    sp.GetRequiredService<ILogger<ATProtoStorageService>>( )
                )
            );

            return services;
        }

        /// <summary>
        /// Adds ATProto storage services if credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="atProtoIdentifier">ATProto identifier (handle or DID).</param>
        /// <param name="atProtoPassword">ATProto app password.</param>
        /// <returns>The configured service collection.</returns>
        [Obsolete( "Use AddATProtoSessionManager() followed by AddATProtoStorage() overload without parameters instead." )]
        public static IServiceCollection AddATProtoStorage(
            this IServiceCollection services,
            string? atProtoIdentifier,
            string? atProtoPassword
        ) {
            if (string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
                string.IsNullOrWhiteSpace( atProtoPassword )) {
                return services;
            }

            // Register session manager first, then storage
            _ = services.AddATProtoSessionManager( atProtoIdentifier, atProtoPassword );
            _ = services.AddATProtoStorage( );

            return services;
        }
    }
}
