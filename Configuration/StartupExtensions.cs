using System.Text.Json;
using Microsoft.Extensions.Http.Resilience;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Polly;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.Services;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Configuration {

    /// <summary>
    /// Extension methods for configuring the TuneBridge services and HTTP client resilience (retry) policies.
    /// </summary>
    internal static class StartupExtensions {

        /// <summary>
        /// Configures the application settings by adding command line arguments, environment variables, and the appsettings.json file.
        /// </summary>
        /// <param name="config">The configuration builder to extend.</param>
        /// <param name="args">Command line arguments passed to the application.</param>
        /// <returns>The updated <see cref="IConfigurationBuilder"/>.</returns>
        public static IConfigurationBuilder ConfigureAppSettings(
            this IConfigurationBuilder config,
            string[] args
        ) {
            return config.AddCommandLine( args )
                .AddEnvironmentVariables( )
                .AddJsonFile(
                    path: Path.Join( Path.GetDirectoryName( Environment.ProcessPath ), "appsettings.json" ),
                    optional: false,
                    reloadOnChange: true
                );
        }

        /// <summary>
        /// Registers TuneBridge services, authentication handlers, HTTP clients, and (optional) Discord services.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="config">The application configuration containing TuneBridge settings.</param>
        /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection AddTuneBridgeServices(
            this IServiceCollection services,
            IConfiguration config
        ) {
            AppSettings settings = new();
            config.GetRequiredSection( "TuneBridge" ).Bind( settings );
            _ = services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

            HashSet<SupportedProviders> enabledProviders = [];

            // Register Apple Music if credentials are present.
            if (string.IsNullOrWhiteSpace( settings.AppleTeamId ) == false &&
                string.IsNullOrWhiteSpace( settings.AppleKeyId ) == false
            ) {
                // Fail fast if missing required apple key file.
                FileInfo keyPath = new(settings.AppleKeyPath);
                if (!keyPath.Exists) {
                    throw new FileNotFoundException( $"Missing .p8 file at: {keyPath.FullName}" );
                }

                // Fail fast if apple key file is empty.
                string keyContents = File.ReadAllText(keyPath.FullName);
                if (string.IsNullOrWhiteSpace( keyContents )) {
                    throw new InvalidDataException( $".p8 file missing contents at: {keyPath.FullName}" );
                }

                _ = services.AddHttpClient( "musickit-api", c => {
                    c.BaseAddress = new Uri( "https://api.music.apple.com/v1/catalog/" );
                } ).AddStandardResilience( );

                _ = services.AddSingleton( new AppleJwtHandler( settings.AppleTeamId, settings.AppleKeyId, keyContents ) );
                _ = services.AddTransient<AppleMusicLookupService>( );

                _ = enabledProviders.Add( SupportedProviders.AppleMusic );
            } else {
                Console.WriteLine( "TuneBridge: Music lookup service for Apple Music disabled due to invalid input credentials." );
            }

            // Register Spotify if credentials are present.
            if (string.IsNullOrWhiteSpace( settings.SpotifyClientId ) == false &&
                string.IsNullOrWhiteSpace( settings.SpotifyClientSecret ) == false
            ) {
                _ = services.AddHttpClient( "spotify-auth", c => {
                    c.BaseAddress = new Uri( "https://accounts.spotify.com/" );
                } ).AddStandardResilience( );

                _ = services.AddHttpClient( "spotify-api", c => {
                    c.BaseAddress = new Uri( "https://api.spotify.com/v1/" );
                } ).AddStandardResilience( );

                _ = services.AddSingleton( new SpotifyCredentials( settings.SpotifyClientId, settings.SpotifyClientSecret ) );
                _ = services.AddTransient<SpotifyTokenHandler>( );
                _ = services.AddTransient<SpotifyLookupService>( );

                _ = enabledProviders.Add( SupportedProviders.Spotify );
            } else {
                Console.WriteLine( "TuneBridge: Music lookup service for Spotify disabled due to invalid input credentials." );
            }

            // Register Tidal if credentials are present.
            if (string.IsNullOrWhiteSpace( settings.TidalClientId ) == false &&
                string.IsNullOrWhiteSpace( settings.TidalClientSecret ) == false
            ) {
                _ = services.AddHttpClient( "tidal-auth", c => {
                    c.BaseAddress = new Uri( "https://auth.tidal.com/" );
                } ).AddStandardResilience( );

                _ = services.AddHttpClient( "tidal-api", c => {
                    c.BaseAddress = new Uri( "https://openapi.tidal.com/v1/" );
                } ).AddStandardResilience( );

                _ = services.AddSingleton( new TidalCredentials( settings.TidalClientId, settings.TidalClientSecret ) );
                _ = services.AddTransient<TidalTokenHandler>( );
                _ = services.AddTransient<TidalLookupService>( );

                _ = enabledProviders.Add( SupportedProviders.Tidal );
            } else {
                Console.WriteLine( "TuneBridge: Music lookup service for Tidal disabled due to invalid input credentials." );
            }

            // Validate that at least one provider is enabled.
            if (enabledProviders.Count == 0) {
                throw new InvalidOperationException( "Required settings are missing. Cannot add TuneBridge services if no IMusicLookupService(s) are available." );
            }

            // Add the DefaultMediaLinkService with enabled IMusicLookupService(s).
            _ = services.AddTransient<IMediaLinkService>( s => new DefaultMediaLinkService(
                GetEnabledProviderServices( enabledProviders, s ),
                s.GetRequiredService<ILogger<DefaultMediaLinkService>>( ),
                s.GetRequiredService<JsonSerializerOptions>( )
            ) );
            _ = services.AddSingleton( enabledProviders );


            _ = services.AddTransient( s => new DiscordNodeConfig( s.GetRequiredService<IMediaLinkService>( ), settings.NodeNumber ) );

            // Register Discord if credentials are present.
            if (string.IsNullOrWhiteSpace( settings.DiscordToken ) == false) {
                _ = services.AddDiscordShardedGateway( options => {
                    options.Token = settings.DiscordToken;
                    options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
                    options.ShardCount = null; // let discord decide for us
                } );
                _ = services.AddShardedGatewayHandlers( typeof( Program ).Assembly );
            } else {
                Console.WriteLine( "TuneBridge: Discord services disabled due to invalid input credentials." );
            }

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
                        results.Add( SupportedProviders.AppleMusic, serviceProvider.GetRequiredService<AppleMusicLookupService>( ) );
                        break;
                    case SupportedProviders.Spotify:
                        results.Add( SupportedProviders.Spotify, serviceProvider.GetRequiredService<SpotifyLookupService>( ) );
                        break;
                    case SupportedProviders.Tidal:
                        results.Add( SupportedProviders.Tidal, serviceProvider.GetRequiredService<TidalLookupService>( ) );
                        break;
                }
            }
            return results;
        }

        /// <summary>
        /// Adds a standard http client resilience pipeline to the builder, configuring retry and timeout policies.
        /// </summary>
        /// <param name="builder">The HTTP client builder to configure.</param>
        /// <returns>The configured <see cref="IHttpStandardResiliencePipelineBuilder"/>.</returns>
        internal static IHttpStandardResiliencePipelineBuilder AddStandardResilience( this IHttpClientBuilder builder ) {
            return builder.AddStandardResilienceHandler( options => {
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.MaxRetryAttempts = 5;
                options.Retry.Delay = TimeSpan.FromSeconds( 1 );
                options.Retry.MaxDelay = TimeSpan.FromSeconds( 30 );
                options.Retry.ShouldRetryAfterHeader = true; // honor Retry-After

                options.Retry.DisableForUnsafeHttpMethods( ); // Disables retry on POST/PUT/PATCH/DELETE/CONNECT

                // Timeouts (outer total, inner per-attempt)
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds( 20 );
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds( 10 );
            } );
        }
    }
}
