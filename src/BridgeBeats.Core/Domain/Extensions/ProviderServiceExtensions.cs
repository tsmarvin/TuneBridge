using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.AppleMusic;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Domain.Providers.Tidal;
using idunno.Security;

namespace BridgeBeats.Core.Domain.Extensions {

    /// <summary>
    /// Dependency-injection wiring for the music providers. Two registration families live here: the
    /// <c>Add*Services</c> methods register the <b>direct</b> provider services that call the real
    /// Spotify, Apple Music, and Tidal HTTP APIs (with their named HTTP clients, credentials, auth
    /// handlers, and delegating handlers); the <c>Add*WorkerHttpClient</c> methods register the
    /// <b>proxy</b> services that forward lookups to remote provider workers over HTTP.
    /// </summary>
    public static class ProviderServiceExtensions {

        /// <summary>
        /// Builds a factory that creates a <c>RetryAfterLimitHandler</c> bound to the given threshold,
        /// resolving a typed logger from the service provider at creation time. Used when wiring a
        /// provider's named HTTP clients.
        /// </summary>
        /// <param name="maxRetryAfterSeconds">
        /// The <c>Retry-After</c> threshold, in seconds, above which the handler fails fast.
        /// </param>
        /// <returns>A factory that produces the configured handler from an <see cref="IServiceProvider"/>.</returns>
        internal static Func<IServiceProvider, RetryAfterLimitHandler> CreateRetryAfterLimitHandlerFactory( int maxRetryAfterSeconds )
            => sp => new RetryAfterLimitHandler(
                maxRetryAfterSeconds,
                sp.GetRequiredService<ILoggerFactory>( ).CreateLogger<RetryAfterLimitHandler>( )
            );

        #region Service Provider Registration

        /// <summary>
        /// Registers the direct Apple Music lookup service using private-key contents supplied by
        /// the authoritative application-settings store.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="teamId">Apple developer team id.</param>
        /// <param name="keyId">Apple MusicKit key id.</param>
        /// <param name="privateKey">The Apple <c>.p8</c> private-key contents.</param>
        /// <param name="enabledProviders">The running set of enabled providers, mutated on success.</param>
        /// <param name="maxRetryAfterSeconds">The <c>Retry-After</c> fail-fast threshold in seconds.</param>
        /// <returns><see langword="true"/> when Apple Music was registered.</returns>
        public static bool AddAppleMusicServicesFromPrivateKey(
            this IServiceCollection services,
            string? teamId,
            string? keyId,
            string? privateKey,
            HashSet<SupportedProviders> enabledProviders,
            int maxRetryAfterSeconds = 120
        ) {
            if (!services.AddAppleMusicJwtHandlerFromPrivateKey( teamId, keyId, privateKey, maxRetryAfterSeconds )) {
                return false;
            }

            _ = services.AddTransient<AppleMusicLookupService>( );
            _ = enabledProviders.Add( SupportedProviders.AppleMusic );
            return true;
        }

        /// <summary>
        /// Registers the Apple Music JWT handler using private-key contents supplied directly by
        /// the protected application-settings store.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="teamId">Apple developer team id.</param>
        /// <param name="keyId">Apple MusicKit key id.</param>
        /// <param name="privateKey">The Apple <c>.p8</c> private-key contents.</param>
        /// <param name="maxRetryAfterSeconds">The <c>Retry-After</c> fail-fast threshold in seconds.</param>
        /// <returns><see langword="true"/> when the handler and client were registered.</returns>
        public static bool AddAppleMusicJwtHandlerFromPrivateKey(
            this IServiceCollection services,
            string? teamId,
            string? keyId,
            string? privateKey,
            int maxRetryAfterSeconds = 120
        ) {
            if (string.IsNullOrWhiteSpace( teamId ) ||
                string.IsNullOrWhiteSpace( keyId ) ||
                string.IsNullOrWhiteSpace( privateKey )) {
                return false;
            }

            _ = services.AddHttpClient( "musickit-api", c => {
                c.BaseAddress = new Uri( "https://api.music.apple.com/v1/catalog/" );
            } )
            .ConfigurePrimaryHttpMessageHandler( ( ) => SsrfSocketsHttpHandlerFactory.Create(
                connectTimeout: TimeSpan.FromSeconds( 10 ) ) )
            .ConfigureAdditionalHttpMessageHandlers( ( handlers, _ ) =>
                handlers.Insert( 0, new TerminalProviderRateLimitHandler( SupportedProviders.AppleMusic ) ) )
            .AddHttpMessageHandler( CreateRetryAfterLimitHandlerFactory( maxRetryAfterSeconds ) )
            .AddHttpMessageHandler( ( ) => new ProviderMetricsHandler( "applemusic" ) );

            _ = services.AddSingleton( new AppleJwtHandler( teamId, keyId, privateKey ) );

            return true;
        }

        /// <summary>
        /// Registers the direct Spotify lookup service. Wires the <c>spotify-auth</c> and
        /// <c>spotify-api</c> named HTTP clients (SSRF-hardened primary handler,
        /// <c>RetryAfterLimitHandler</c>, and <c>ProviderMetricsHandler</c>), the
        /// <c>SpotifyCredentials</c> singleton, and the <c>SpotifyTokenHandler</c>. Also registers
        /// <c>ISpotifyBulkLookupService</c> resolving through <c>SpotifyLookupService</c> so the bulk
        /// processor can depend on the interface rather than the concrete class, and adds
        /// <see cref="SupportedProviders.Spotify"/> to <paramref name="enabledProviders"/>.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="clientId">Spotify client id.</param>
        /// <param name="clientSecret">Spotify client secret.</param>
        /// <param name="enabledProviders">The running set of enabled providers, mutated on success.</param>
        /// <param name="maxRetryAfterSeconds">The <c>Retry-After</c> fail-fast threshold in seconds. Defaults to 120.</param>
        /// <returns><see langword="true"/> when Spotify was registered; <see langword="false"/> when the client id or secret was blank.</returns>
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
            .ConfigureAdditionalHttpMessageHandlers( ( handlers, _ ) =>
                handlers.Insert( 0, new TerminalProviderRateLimitHandler( SupportedProviders.Spotify ) ) )
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
        /// Registers the direct Tidal lookup service. Wires the <c>tidal-auth</c> and <c>tidal-api</c>
        /// named HTTP clients (SSRF-hardened primary handler, <c>RetryAfterLimitHandler</c>, and
        /// <c>ProviderMetricsHandler</c>), the <c>TidalCredentials</c> singleton, and the
        /// <c>TidalTokenHandler</c>. Adds <see cref="SupportedProviders.Tidal"/> to
        /// <paramref name="enabledProviders"/> on success.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="clientId">Tidal client id.</param>
        /// <param name="clientSecret">Tidal client secret.</param>
        /// <param name="enabledProviders">The running set of enabled providers, mutated on success.</param>
        /// <param name="maxRetryAfterSeconds">The <c>Retry-After</c> fail-fast threshold in seconds. Defaults to 120.</param>
        /// <returns><see langword="true"/> when Tidal was registered; <see langword="false"/> when the client id or secret was blank.</returns>
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
            .ConfigureAdditionalHttpMessageHandlers( ( handlers, _ ) =>
                handlers.Insert( 0, new TerminalProviderRateLimitHandler( SupportedProviders.Tidal ) ) )
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

        /// <summary>Named HTTP client used by the web app to reach the Spotify worker service.</summary>
        public const string SpotifyWorkerHttpClientName = "spotify-worker";

        /// <summary>Named HTTP client used by the web app to reach the Apple Music worker service.</summary>
        public const string AppleMusicWorkerHttpClientName = "applemusic-worker";

        /// <summary>Named HTTP client used by the web app to reach the Tidal worker service.</summary>
        public const string TidalWorkerHttpClientName = "tidal-worker";

        /// <summary>
        /// Registers the <b>proxy</b> lookup services for each enabled worker. Each proxy forwards
        /// lookups over HTTP to a remote provider worker through its <c>{provider}-worker</c> named
        /// client, rather than calling the real provider API directly.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="spotifyWorkerEnabled">Whether to register the Spotify worker proxy.</param>
        /// <param name="appleMusicWorkerEnabled">Whether to register the Apple Music worker proxy.</param>
        /// <param name="tidalWorkerEnabled">Whether to register the Tidal worker proxy.</param>
        /// <returns>The set of providers whose worker proxies were enabled.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no worker was enabled.</exception>
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
        /// Registers the <c>spotify-worker</c> named HTTP client and the
        /// <c>SpotifyHttpLookupService</c> proxy that forwards lookups to the Spotify worker. Adds
        /// <see cref="SupportedProviders.Spotify"/> to <paramref name="enabledProviders"/>.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="enabledProviders">The running set of enabled providers, mutated on success.</param>
        /// <returns>Always <see langword="true"/>.</returns>
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
        /// Registers the <c>applemusic-worker</c> named HTTP client and the
        /// <c>AppleMusicHttpLookupService</c> proxy that forwards lookups to the Apple Music worker.
        /// Adds <see cref="SupportedProviders.AppleMusic"/> to <paramref name="enabledProviders"/>.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="enabledProviders">The running set of enabled providers, mutated on success.</param>
        /// <returns>Always <see langword="true"/>.</returns>
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
        /// Registers the <c>tidal-worker</c> named HTTP client and the <c>TidalHttpLookupService</c>
        /// proxy that forwards lookups to the Tidal worker. Adds
        /// <see cref="SupportedProviders.Tidal"/> to <paramref name="enabledProviders"/>.
        /// </summary>
        /// <param name="services">The service collection to add registrations to.</param>
        /// <param name="enabledProviders">The running set of enabled providers, mutated on success.</param>
        /// <returns>Always <see langword="true"/>.</returns>
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
