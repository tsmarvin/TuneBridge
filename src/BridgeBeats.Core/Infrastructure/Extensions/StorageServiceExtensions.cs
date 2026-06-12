using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Storage;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Extensions {

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
        /// Registers a named HTTP client for the sync (getRepo) endpoint with a 130-second transport timeout.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddATProtoStorage( this IServiceCollection services ) {
            // CAR downloads (com.atproto.sync.getRepo) join the global resilience pipeline.
            // HttpClient.Timeout is a transport backstop above the global 120s AttemptTimeout.
            _ = services
                .AddHttpClient( ATProtoStorageService.ATProtoSyncHttpClientName )
                .ConfigureHttpClient( c => c.Timeout = TimeSpan.FromSeconds( 130 ) );

            _ = services.AddSingleton<IATProtoStorageService>( sp =>
                new ATProtoStorageService(
                    sp.GetRequiredService<IATProtoSessionManager>( ),
                    sp.GetRequiredService<ILogger<ATProtoStorageService>>( ),
                    sp.GetRequiredService<IHttpClientFactory>( )
                )
            );

            return services;
        }
    }
}
