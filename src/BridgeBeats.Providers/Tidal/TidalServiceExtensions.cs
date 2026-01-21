using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.Common;
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
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>True if Tidal services were registered; otherwise false.</returns>
        public static bool AddTidalServices(
            this IServiceCollection services,
            string? clientId,
            string? clientSecret,
            HashSet<SupportedProviders> enabledProviders,
            int maxRetryAfterSeconds = 120
        ) {
            if (string.IsNullOrWhiteSpace( clientId ) ||
                string.IsNullOrWhiteSpace( clientSecret )) {
                return false;
            }

            Func<IServiceProvider, RetryAfterLimitHandler> handlerFactory = ProviderServiceExtensions.CreateRetryAfterLimitHandlerFactory( maxRetryAfterSeconds );

            _ = services.AddHttpClient( "tidal-auth", c => {
                c.BaseAddress = new Uri( "https://auth.tidal.com/" );
            } )
            .AddHttpMessageHandler( handlerFactory )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "tidal" ) );

            _ = services.AddHttpClient( "tidal-api", c => {
                c.BaseAddress = new Uri( "https://openapi.tidal.com/v2/" );
            } )
            .AddHttpMessageHandler( handlerFactory )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "tidal" ) );

            _ = services.AddSingleton( new TidalCredentials( clientId, clientSecret ) );
            _ = services.AddTransient<TidalTokenHandler>( );
            _ = services.AddTransient<TidalLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.Tidal );
            return true;
        }

    }

}
