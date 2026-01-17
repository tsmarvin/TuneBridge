using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Infrastructure.Storage {

    /// <summary>
    /// Extension methods for registering ATProto storage services.
    /// </summary>
    public static class StorageServiceExtensions {

        /// <summary>
        /// Adds ATProto storage services if credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="atProtoIdentifier">ATProto identifier (handle or DID).</param>
        /// <param name="atProtoPassword">ATProto app password.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddATProtoStorage(
            this IServiceCollection services,
            string? atProtoIdentifier,
            string? atProtoPassword
        ) {
            if (string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
                string.IsNullOrWhiteSpace( atProtoPassword )) {
                return services;
            }

            _ = services.AddSingleton<IATProtoStorageService>( s =>
                new ATProtoStorageService(
                    atProtoIdentifier,
                    atProtoPassword,
                    s.GetRequiredService<ILogger<ATProtoStorageService>>( )
                )
            );

            return services;
        }
    }
}
