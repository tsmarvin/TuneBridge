using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.AppleMusic;
using BridgeBeats.Providers.Spotify;
using BridgeBeats.Providers.Tidal;
using Microsoft.Extensions.DependencyInjection;

namespace BridgeBeats.Providers {

    /// <summary>
    /// Unified extension methods for registering all music provider services.
    /// </summary>
    public static class ProviderServiceExtensions {

        /// <summary>
        /// Registers all configured music provider services (Apple Music, Spotify, Tidal).
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="appleTeamId">The Apple Developer Team ID.</param>
        /// <param name="appleKeyId">The Apple Music API Key ID.</param>
        /// <param name="appleKeyPath">The path to the Apple .p8 private key file.</param>
        /// <param name="spotifyClientId">The Spotify API Client ID.</param>
        /// <param name="spotifyClientSecret">The Spotify API Client Secret.</param>
        /// <param name="tidalClientId">The Tidal API Client ID.</param>
        /// <param name="tidalClientSecret">The Tidal API Client Secret.</param>
        /// <returns>A set of enabled providers based on the credentials provided.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no providers are configured.</exception>
        public static HashSet<SupportedProviders> AddMusicProviders(
            this IServiceCollection services,
            string? appleTeamId,
            string? appleKeyId,
            string? appleKeyPath,
            string? spotifyClientId,
            string? spotifyClientSecret,
            string? tidalClientId,
            string? tidalClientSecret
        ) {
            HashSet<SupportedProviders> enabledProviders = [ ];

            _ = services.AddAppleMusicServices( appleTeamId, appleKeyId, appleKeyPath, enabledProviders );
            _ = services.AddSpotifyServices( spotifyClientId, spotifyClientSecret, enabledProviders );
            _ = services.AddTidalServices( tidalClientId, tidalClientSecret, enabledProviders );

            return enabledProviders.Count == 0
                ? throw new InvalidOperationException(
                    "Required settings are missing. Cannot add BridgeBeats services if no IMusicLookupService(s) are available."
                )
                : enabledProviders;
        }

    }

}
