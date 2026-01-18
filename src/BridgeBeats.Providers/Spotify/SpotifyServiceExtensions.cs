using BridgeBeats.Contracts.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Providers.Spotify {

    /// <summary>
    /// Extension methods for registering Spotify provider services.
    /// </summary>
    public static class SpotifyServiceExtensions {

        /// <summary>
        /// Registers Spotify services if valid credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="clientId">The Spotify API Client ID.</param>
        /// <param name="clientSecret">The Spotify API Client Secret.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <returns>True if Spotify services were registered; otherwise false.</returns>
        public static bool AddSpotifyServices(
            this IServiceCollection services,
            string? clientId,
            string? clientSecret,
            HashSet<SupportedProviders> enabledProviders
        ) {
            if (string.IsNullOrWhiteSpace( clientId ) ||
                string.IsNullOrWhiteSpace( clientSecret )) {
                return false;
            }

            _ = services.AddHttpClient( "spotify-auth", c => {
                c.BaseAddress = new Uri( "https://accounts.spotify.com/" );
            } );

            _ = services.AddHttpClient( "spotify-api", c => {
                c.BaseAddress = new Uri( "https://api.spotify.com/v1/" );
            } );

            _ = services.AddSingleton( new SpotifyCredentials( clientId, clientSecret ) );
            _ = services.AddTransient<SpotifyTokenHandler>( );
            _ = services.AddTransient<SpotifyLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.Spotify );
            return true;
        }

    }

}
