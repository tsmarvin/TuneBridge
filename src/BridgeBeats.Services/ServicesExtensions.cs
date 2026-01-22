using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Services {

    /// <summary>
    /// Extension methods for registering all BridgeBeats services.
    /// </summary>
    public static class ServicesExtensions {

        /// <summary>
        /// Registers all BridgeBeats services including link resolver and card services.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="enabledProviders">Set of enabled music providers.</param>
        /// <param name="useCaching">Whether to use caching for media link service.</param>
        /// <param name="baseUrl">The base URL for card and playlist URLs.</param>
        /// <param name="cardCacheExpirationHours">Card cache expiration in hours.</param>
        /// <param name="cardCacheCleanupInterval">Card cache cleanup interval.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddBridgeBeatsServices(
            this IServiceCollection services,
            HashSet<SupportedProviders> enabledProviders,
            bool useCaching,
            string baseUrl,
            int cardCacheExpirationHours,
            int cardCacheCleanupInterval
        ) {
            _ = services.AddMediaLinkResolver( enabledProviders, useCaching );
            _ = services.AddCardServices( baseUrl, cardCacheExpirationHours, cardCacheCleanupInterval );
            _ = services.AddQrCodeService( );

            return services;
        }

        /// <summary>
        /// Registers the QR code generation service.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddQrCodeService( this IServiceCollection services ) {
            _ = services.AddSingleton<IQrCodeService, QrCodeService>( );

            return services;
        }
    }
}
