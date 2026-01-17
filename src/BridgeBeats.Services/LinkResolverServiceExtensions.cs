using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Services.LinkResolver;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Services {

    /// <summary>
    /// Extension methods for registering link resolver services.
    /// </summary>
    public static class LinkResolverServiceExtensions {

        /// <summary>
        /// Registers the media link resolver service (with optional caching).
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="enabledProviders">Set of enabled music providers.</param>
        /// <param name="useCaching">Whether to use caching (requires ATProto configuration).</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddMediaLinkResolver(
            this IServiceCollection services,
            HashSet<SupportedProviders> enabledProviders,
            bool useCaching
        ) {
            _ = services.AddTransient<IMediaLinkService>( s => {
                Dictionary<SupportedProviders, IMusicLookupService> providerServices = GetEnabledProviderServices( enabledProviders, s );

                // Use caching service if ATProto is configured, otherwise use default service
                return useCaching
                    ? new CachingMediaLinkService(
                        providerServices,
                        s.GetRequiredService<IMediaLinkCacheRepository>( ),
                        s.GetRequiredService<ILogger<CachingMediaLinkService>>( ),
                        s.GetRequiredService<JsonSerializerOptions>( )
                    )
                    : new DefaultMediaLinkService(
                        providerServices,
                        s.GetRequiredService<ILogger<DefaultMediaLinkService>>( ),
                        s.GetRequiredService<JsonSerializerOptions>( )
                    );
            } );

            return services;
        }

        /// <summary>
        /// Creates a dictionary that maps enabled providers (by their enums designation) to their corresponding
        /// <see cref="IMusicLookupService"/> implementations.
        /// This is a helper method used during service registration to enable adding the
        /// <see cref="IMusicLookupService"/> implementations to the <see cref="DefaultMediaLinkService"/>.
        /// </summary>
        /// <param name="enabledProviders">The set of providers that have been enabled based on configuration.</param>
        /// <param name="serviceProvider">The service provider used to resolve service instances.</param>
        /// <returns>A dictionary of provider to service instances.</returns>
        private static Dictionary<SupportedProviders, IMusicLookupService> GetEnabledProviderServices(
            HashSet<SupportedProviders> enabledProviders,
            IServiceProvider serviceProvider
        ) {
            Dictionary<SupportedProviders, IMusicLookupService> results = [];
            foreach (SupportedProviders provider in enabledProviders) {
                switch (provider) {
                    case SupportedProviders.AppleMusic:
                        results.Add( SupportedProviders.AppleMusic, serviceProvider.GetRequiredService<Providers.AppleMusic.AppleMusicLookupService>( ) );
                        break;
                    case SupportedProviders.Spotify:
                        results.Add( SupportedProviders.Spotify, serviceProvider.GetRequiredService<Providers.Spotify.SpotifyLookupService>( ) );
                        break;
                    case SupportedProviders.Tidal:
                        results.Add( SupportedProviders.Tidal, serviceProvider.GetRequiredService<Providers.Tidal.TidalLookupService>( ) );
                        break;
                }
            }
            return results;
        }
    }
}
