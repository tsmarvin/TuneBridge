using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.AppleMusic;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Domain.Providers.Tidal;
using idunno.Security;

namespace BridgeBeats.Core.Domain.Extensions {

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

        #region Service Provider Registration


        /// <summary>
        /// Registers Apple Music services if valid credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="teamId">The Apple Developer Team ID.</param>
        /// <param name="keyId">The Apple Music API Key ID.</param>
        /// <param name="keyPath">The path to the .p8 private key file.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>True if Apple Music services were registered; otherwise false.</returns>
        public static bool AddAppleMusicServices(
            this IServiceCollection services,
            string? teamId,
            string? keyId,
            string? keyPath,
            HashSet<SupportedProviders> enabledProviders,
            int maxRetryAfterSeconds = 120
        ) {
            if (!services.AddAppleMusicJwtHandler( teamId, keyId, keyPath, maxRetryAfterSeconds )) {
                return false;
            }

            _ = services.AddTransient<AppleMusicLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.AppleMusic );
            return true;
        }

        /// <summary>
        /// Registers the AppleJwtHandler and musickit-api HTTP client for MusicKit JS authentication.
        /// This is needed for the AppleMusicController to generate developer tokens, regardless of
        /// whether worker services are used for backend lookups.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="teamId">The Apple Developer Team ID.</param>
        /// <param name="keyId">The Apple Music API Key ID.</param>
        /// <param name="keyPath">The path to the .p8 private key file.</param>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>True if the JWT handler was registered; otherwise false.</returns>
        public static bool AddAppleMusicJwtHandler(
            this IServiceCollection services,
            string? teamId,
            string? keyId,
            string? keyPath,
            int maxRetryAfterSeconds = 120
        ) {
            // Check if Apple Music credentials are provided
            if (string.IsNullOrWhiteSpace( teamId ) ||
                string.IsNullOrWhiteSpace( keyId )) {
                return false;
            }

            // If Team ID and Key ID are provided, the key path must also be provided and valid
            if (string.IsNullOrWhiteSpace( keyPath )) {
                return false;
            }

            // Fail fast if missing required apple key file.
            FileInfo keyFile = new( keyPath );
            if (!keyFile.Exists) {
                throw new FileNotFoundException( $"Missing .p8 file at: {keyFile.FullName}" );
            }

            // Fail fast if apple key file is empty.
            string keyContents = File.ReadAllText( keyFile.FullName );
            if (string.IsNullOrWhiteSpace( keyContents )) {
                throw new InvalidDataException( $".p8 file missing contents at: {keyFile.FullName}" );
            }

            _ = services.AddHttpClient( "musickit-api", c => {
                c.BaseAddress = new Uri( "https://api.music.apple.com/v1/catalog/" );
            } )
            .ConfigurePrimaryHttpMessageHandler( ( ) => SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 10 ) ) )
            .AddHttpMessageHandler( CreateRetryAfterLimitHandlerFactory( maxRetryAfterSeconds ) )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "applemusic" ) );

            _ = services.AddSingleton( new AppleJwtHandler( teamId, keyId, keyContents ) );

            return true;
        }

        /// <summary>
        /// Registers Spotify services if valid credentials are provided.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="clientId">The Spotify API Client ID.</param>
        /// <param name="clientSecret">The Spotify API Client Secret.</param>
        /// <param name="enabledProviders">The set of enabled providers to update.</param>
        /// <param name="maxRetryAfterSeconds">Maximum Retry-After value in seconds before failing fast.</param>
        /// <returns>True if Spotify services were registered; otherwise false.</returns>
        public static bool AddSpotifyServices(
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

            Func<IServiceProvider, RetryAfterLimitHandler> handlerFactory = CreateRetryAfterLimitHandlerFactory( maxRetryAfterSeconds );

            _ = services.AddHttpClient( "spotify-auth", c => {
                c.BaseAddress = new Uri( "https://accounts.spotify.com/" );
            } )
            .ConfigurePrimaryHttpMessageHandler( ( ) => SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 10 ) ) )
            .AddHttpMessageHandler( handlerFactory )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "spotify" ) );

            _ = services.AddHttpClient( "spotify-api", c => {
                c.BaseAddress = new Uri( "https://api.spotify.com/v1/" );
            } )
            .ConfigurePrimaryHttpMessageHandler( ( ) => SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 10 ) ) )
            .AddHttpMessageHandler( handlerFactory )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "spotify" ) );

            _ = services.AddSingleton( new SpotifyCredentials( clientId, clientSecret ) );
            _ = services.AddTransient<SpotifyTokenHandler>( );
            _ = services.AddTransient<SpotifyLookupService>( );
            // Register as ISpotifyBulkLookupService so SpotifyBulkProcessorService
            // can receive a testable interface rather than the sealed concrete class.
            _ = services.AddTransient<ISpotifyBulkLookupService>( sp => sp.GetRequiredService<SpotifyLookupService>( ) );

            _ = enabledProviders.Add( SupportedProviders.Spotify );
            return true;
        }


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

            Func<IServiceProvider, RetryAfterLimitHandler> handlerFactory = CreateRetryAfterLimitHandlerFactory( maxRetryAfterSeconds );

            _ = services.AddHttpClient( "tidal-auth", c => {
                c.BaseAddress = new Uri( "https://auth.tidal.com/" );
            } )
            .ConfigurePrimaryHttpMessageHandler( ( ) => SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 10 ) ) )
            .AddHttpMessageHandler( handlerFactory )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "tidal" ) );

            _ = services.AddHttpClient( "tidal-api", c => {
                c.BaseAddress = new Uri( "https://openapi.tidal.com/v2/" );
            } )
            .ConfigurePrimaryHttpMessageHandler( ( ) => SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 10 ) ) )
            .AddHttpMessageHandler( handlerFactory )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "tidal" ) );

            _ = services.AddSingleton( new TidalCredentials( clientId, clientSecret ) );
            _ = services.AddTransient<TidalTokenHandler>( );
            _ = services.AddTransient<TidalLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.Tidal );
            return true;
        }

        #endregion Service Provider Registration

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
            // Register named HTTP client with base address for Aspire service discovery
            _ = services.AddHttpClient( SpotifyWorkerHttpClientName, client => {
                client.BaseAddress = new Uri( "http://spotify-worker" );
            } );

            // Register the HTTP adapter as the IMusicLookupService for Spotify
            _ = services.AddTransient( sp =>
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
            // Register named HTTP client with base address for Aspire service discovery
            _ = services.AddHttpClient( AppleMusicWorkerHttpClientName, client => {
                client.BaseAddress = new Uri( "http://applemusic-worker" );
            } );

            // Register the HTTP adapter as the IMusicLookupService for Apple Music
            _ = services.AddTransient( sp =>
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
            // Register named HTTP client with base address for Aspire service discovery
            _ = services.AddHttpClient( TidalWorkerHttpClientName, client => {
                client.BaseAddress = new Uri( "http://tidal-worker" );
            } );

            // Register the HTTP adapter as the IMusicLookupService for Tidal
            _ = services.AddTransient( sp =>
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
}
