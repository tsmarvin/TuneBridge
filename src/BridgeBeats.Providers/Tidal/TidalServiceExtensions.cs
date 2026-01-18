using BridgeBeats.Contracts.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Providers.Tidal {

    /// <summary>
    /// Extension methods for registering Tidal provider services.
    /// </summary>
    public static class TidalServiceExtensions {

        /// <summary>
        /// Registers Tidal services if valid credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="clientId">The Tidal API Client ID.</param>
        /// <param name="clientSecret">The Tidal API Client Secret.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <returns>True if Tidal services were registered; otherwise false.</returns>
        public static bool AddTidalServices(
            this IServiceCollection services,
            string? clientId,
            string? clientSecret,
            HashSet<SupportedProviders> enabledProviders
        ) {
            if (string.IsNullOrWhiteSpace( clientId ) ||
                string.IsNullOrWhiteSpace( clientSecret )) {
                return false;
            }

            _ = services.AddHttpClient( "tidal-auth", c => {
                c.BaseAddress = new Uri( "https://auth.tidal.com/" );
            } );

            _ = services.AddHttpClient( "tidal-api", c => {
                c.BaseAddress = new Uri( "https://openapi.tidal.com/v2/" );
            } );

            _ = services.AddSingleton( new TidalCredentials( clientId, clientSecret ) );
            _ = services.AddTransient<TidalTokenHandler>( );
            _ = services.AddTransient<TidalLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.Tidal );
            return true;
        }

    }

}
