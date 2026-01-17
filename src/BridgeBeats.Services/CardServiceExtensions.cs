using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Identity;
using BridgeBeats.Services.Cards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Services {

    /// <summary>
    /// Extension methods for registering card and playlist services.
    /// </summary>
    public static class CardServiceExtensions {

        /// <summary>
        /// Registers the OpenGraph card service and playlist service.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="baseUrl">The base URL for card and playlist URLs.</param>
        /// <param name="cardCacheExpirationHours">Card cache expiration in hours.</param>
        /// <param name="cardCacheCleanupInterval">Card cache cleanup interval.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCardServices(
            this IServiceCollection services,
            string baseUrl,
            int cardCacheExpirationHours,
            int cardCacheCleanupInterval
        ) {
            // Validate card cache settings
            if (cardCacheExpirationHours <= 0) {
                throw new InvalidOperationException( $"CardCacheExpirationHours must be greater than zero. Current value: {cardCacheExpirationHours}" );
            }
            if (cardCacheCleanupInterval <= 0) {
                throw new InvalidOperationException( $"CardCacheCleanupInterval must be greater than zero. Current value: {cardCacheCleanupInterval}" );
            }

            // OpenGraph card service
            _ = services.AddSingleton<IOpenGraphCardService, OpenGraphCardService>(
                _ => new OpenGraphCardService(
                    baseUrl,
                    cardCacheExpirationHours,
                    cardCacheCleanupInterval
                )
            );

            // Playlist service (singleton with DbContextFactory for thread-safe database access)
            _ = services.AddSingleton<IPlaylistService, PlaylistService>(
                p => new PlaylistService( baseUrl, p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( ) )
            );

            // Register playlist cleanup background service
            _ = services.AddHostedService<PlaylistCleanupService>( );

            return services;
        }
    }
}
