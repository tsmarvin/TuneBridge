using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.AppleMusic;
using BridgeBeats.Providers.Common;
using BridgeBeats.Providers.Spotify;
using BridgeBeats.Providers.Tidal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
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
            string? tidalClientSecret,
            int maxRetryAfterSeconds = 120
        ) {
            HashSet<SupportedProviders> enabledProviders = [ ];

            _ = services.AddAppleMusicServices( appleTeamId, appleKeyId, appleKeyPath, enabledProviders, maxRetryAfterSeconds );
            _ = services.AddSpotifyServices( spotifyClientId, spotifyClientSecret, enabledProviders, maxRetryAfterSeconds );
            _ = services.AddTidalServices( tidalClientId, tidalClientSecret, enabledProviders, maxRetryAfterSeconds );

            return enabledProviders.Count == 0
                ? throw new InvalidOperationException(
                    "Required settings are missing. Cannot add BridgeBeats services if no IMusicLookupService(s) are available."
                )
                : enabledProviders;
        }

        /// <summary>
        /// Creates a factory function for <see cref="RetryAfterLimitHandler"/> with the specified threshold.
        /// </summary>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>A factory function that creates <see cref="RetryAfterLimitHandler"/> instances.</returns>
        internal static Func<IServiceProvider, RetryAfterLimitHandler> CreateRetryAfterLimitHandlerFactory( int maxRetryAfterSeconds )
            => sp => new RetryAfterLimitHandler(
                maxRetryAfterSeconds,
                sp.GetRequiredService<ILoggerFactory>( ).CreateLogger<RetryAfterLimitHandler>( )
            );

        #region HTTP Client Registration (for main web app calling workers)

        /// <summary>
        /// HTTP client name for the Spotify worker service.
        /// </summary>
        public const string SpotifyWorkerHttpClientName = "spotify-worker";

        /// <summary>
        /// HTTP client name for the Apple Music worker service.
        /// </summary>
        public const string AppleMusicWorkerHttpClientName = "applemusic-worker";

        /// <summary>
        /// HTTP client name for the Tidal worker service.
        /// </summary>
        public const string TidalWorkerHttpClientName = "tidal-worker";

        /// <summary>
        /// Registers HTTP-based music provider clients that communicate with worker services.
        /// Used by the main web application instead of direct provider implementations.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="spotifyWorkerEnabled">Whether to register the Spotify worker client.</param>
        /// <param name="appleMusicWorkerEnabled">Whether to register the Apple Music worker client.</param>
        /// <param name="tidalWorkerEnabled">Whether to register the Tidal worker client.</param>
        /// <returns>A set of enabled providers based on which workers are enabled.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no workers are enabled.</exception>
        public static HashSet<SupportedProviders> AddMusicProviderHttpClients(
            this IServiceCollection services,
            bool spotifyWorkerEnabled,
            bool appleMusicWorkerEnabled,
            bool tidalWorkerEnabled
        ) {
            HashSet<SupportedProviders> enabledProviders = [ ];

            if (spotifyWorkerEnabled) {
                _ = services.AddSpotifyWorkerHttpClient( enabledProviders );
            }

            if (appleMusicWorkerEnabled) {
                _ = services.AddAppleMusicWorkerHttpClient( enabledProviders );
            }

            if (tidalWorkerEnabled) {
                _ = services.AddTidalWorkerHttpClient( enabledProviders );
            }

            return enabledProviders.Count == 0
                ? throw new InvalidOperationException(
                    "Required settings are missing. Cannot add BridgeBeats services if no music provider workers are enabled."
                )
                : enabledProviders;
        }

        /// <summary>
        /// Registers an HTTP client for communicating with the Spotify worker service.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <returns>True if the client was registered.</returns>
        public static bool AddSpotifyWorkerHttpClient(
            this IServiceCollection services,
            HashSet<SupportedProviders> enabledProviders
        ) {
            // Register named HTTP client - base address will be set via Aspire service discovery
            _ = services.AddHttpClient( SpotifyWorkerHttpClientName );

            // Register the HTTP adapter as the IMusicLookupService for Spotify
            _ = services.AddTransient<SpotifyHttpLookupService>( sp =>
                new SpotifyHttpLookupService(
                    sp.GetRequiredService<IHttpClientFactory>( ),
                    sp.GetRequiredService<ILogger<HttpMusicLookupService>>( )
                )
            );

            _ = enabledProviders.Add( SupportedProviders.Spotify );
            return true;
        }

        /// <summary>
        /// Registers an HTTP client for communicating with the Apple Music worker service.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <returns>True if the client was registered.</returns>
        public static bool AddAppleMusicWorkerHttpClient(
            this IServiceCollection services,
            HashSet<SupportedProviders> enabledProviders
        ) {
            // Register named HTTP client - base address will be set via Aspire service discovery
            _ = services.AddHttpClient( AppleMusicWorkerHttpClientName );

            // Register the HTTP adapter as the IMusicLookupService for Apple Music
            _ = services.AddTransient<AppleMusicHttpLookupService>( sp =>
                new AppleMusicHttpLookupService(
                    sp.GetRequiredService<IHttpClientFactory>( ),
                    sp.GetRequiredService<ILogger<HttpMusicLookupService>>( )
                )
            );

            _ = enabledProviders.Add( SupportedProviders.AppleMusic );
            return true;
        }

        /// <summary>
        /// Registers an HTTP client for communicating with the Tidal worker service.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <returns>True if the client was registered.</returns>
        public static bool AddTidalWorkerHttpClient(
            this IServiceCollection services,
            HashSet<SupportedProviders> enabledProviders
        ) {
            // Register named HTTP client - base address will be set via Aspire service discovery
            _ = services.AddHttpClient( TidalWorkerHttpClientName );

            // Register the HTTP adapter as the IMusicLookupService for Tidal
            _ = services.AddTransient<TidalHttpLookupService>( sp =>
                new TidalHttpLookupService(
                    sp.GetRequiredService<IHttpClientFactory>( ),
                    sp.GetRequiredService<ILogger<HttpMusicLookupService>>( )
                )
            );

            _ = enabledProviders.Add( SupportedProviders.Tidal );
            return true;
        }

        #endregion HTTP Client Registration

    }

    /// <summary>
    /// HTTP-based lookup service for Spotify that delegates to the Spotify worker.
    /// </summary>
    public sealed class SpotifyHttpLookupService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpMusicLookupService> logger
    ) : HttpMusicLookupService(
        SupportedProviders.Spotify,
        httpClientFactory,
        ProviderServiceExtensions.SpotifyWorkerHttpClientName,
        logger
    );

    /// <summary>
    /// HTTP-based lookup service for Apple Music that delegates to the Apple Music worker.
    /// </summary>
    public sealed class AppleMusicHttpLookupService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpMusicLookupService> logger
    ) : HttpMusicLookupService(
        SupportedProviders.AppleMusic,
        httpClientFactory,
        ProviderServiceExtensions.AppleMusicWorkerHttpClientName,
        logger
    );

    /// <summary>
    /// HTTP-based lookup service for Tidal that delegates to the Tidal worker.
    /// </summary>
    public sealed class TidalHttpLookupService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpMusicLookupService> logger
    ) : HttpMusicLookupService(
        SupportedProviders.Tidal,
        httpClientFactory,
        ProviderServiceExtensions.TidalWorkerHttpClientName,
        logger
    );

}
