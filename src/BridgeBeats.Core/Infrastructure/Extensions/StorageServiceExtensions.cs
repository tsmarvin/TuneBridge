using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Storage;
using StackExchange.Redis;

namespace BridgeBeats.Core.Infrastructure.Extensions {

    /// <summary>
    /// Dependency-injection registration helpers for the ATProto (Bluesky PDS) session manager
    /// and storage service in the Infrastructure layer.
    /// </summary>
    public static class StorageServiceExtensions {

        /// <summary>
        /// Registers the Redis-backed ATProto session manager as a singleton implementation of
        /// <see cref="BridgeBeats.Contracts.Interfaces.IATProtoSessionManager"/>, using the supplied
        /// service-account credentials. Must be called before <see cref="AddATProtoStorage"/> and
        /// requires a Redis connection.
        /// </summary>
        /// <param name="services">The service collection to add the registration to.</param>
        /// <param name="atProtoIdentifier">The ATProto service-account identifier (handle or DID).</param>
        /// <param name="atProtoPassword">The ATProto service-account password (app password).</param>
        /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
        /// <remarks>
        /// No registration is performed when <paramref name="atProtoIdentifier"/> or
        /// <paramref name="atProtoPassword"/> is null, empty, or whitespace; the method returns the
        /// service collection unchanged. The implementation resolves the shared
        /// <c>IConnectionMultiplexer</c> and a typed logger at activation time and captures the supplied
        /// credentials. Registered with singleton lifetime.
        /// </remarks>
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
        /// Registers the ATProto storage service as a singleton implementation of
        /// <see cref="BridgeBeats.Contracts.Interfaces.IATProtoStorageService"/>, together with the
        /// named HttpClient it uses for repository sync operations. Requires
        /// <see cref="BridgeBeats.Contracts.Interfaces.IATProtoSessionManager"/> to be registered
        /// first via <see cref="AddATProtoSessionManager"/>.
        /// </summary>
        /// <param name="services">The service collection to add the registrations to.</param>
        /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
        /// <remarks>
        /// Configures a named HttpClient (named by <c>ATProtoStorageService.ATProtoSyncHttpClientName</c>)
        /// with a 130-second request timeout for potentially large repository (CAR) downloads. The storage
        /// service resolves the ATProto session manager, a typed logger, and the HttpClient factory at
        /// activation time. Registered with singleton lifetime.
        /// </remarks>
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
