using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Polly;
using TuneBridge.Common;
using TuneBridge.Common.Contracts.Enums;
using TuneBridge.Common.Contracts.Interfaces;
using TuneBridge.Common.Contracts.Records;
using TuneBridge.Core.Domain.Implementations.Auth;
using TuneBridge.Core.Domain.Implementations.Middleware;
using TuneBridge.Core.Domain.Implementations.Services;
using TuneBridge.Core.Domain.Types;
using TuneBridge.Core.Infrastructure.Context;
using TuneBridge.Domain.Implementations.Services;

namespace TuneBridge.Core.Domain.Implementations.Extensions {

    /// <summary>
    /// Extension methods for configuring the TuneBridge services and HTTP client resilience (retry) policies.
    /// </summary>
    public static class StartupExtensions {

        /// <summary>
        /// Registers TuneBridge services, authentication handlers, HTTP clients, and (optional) Discord services.
        /// </summary>
        /// <param name="builder">The builder to configure.</param>
        /// <param name="args">The commandline arguments.</param>
        /// <returns>The configured builder.</returns>
        public static WebApplicationBuilder ConfigureTuneBridgeServices(
            this WebApplicationBuilder builder,
            string[] args
        ) {
            if (builder.Environment.EnvironmentName != "Testing") {
                _ = builder
                    .Configuration
                    .ConfigureAppSettings( args );
                _ = builder.AddCommon( );
            }

            IServiceCollection services = builder.Services;
            IConfiguration config = builder.Configuration;

            _ = AddTuneBridgeServices( services, config );

            return builder;
        }

        /// <summary>
        /// Registers TuneBridge services, authentication handlers, HTTP clients, and (optional) Discord services directly on an IServiceCollection.
        /// This is useful for testing scenarios where you don't need a full IWebHostBuilder.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="config">The configuration to use for settings.</param>
        /// <returns>The configured service collection.</returns>
        public static IServiceCollection AddTuneBridgeServices(
            this IServiceCollection services,
            IConfiguration config
        ) {
            AppSettings settings = new( );
            config.GetRequiredSection( "TuneBridge" ).Bind( settings );

            // Common singletons and caching
            _ = services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

            _ = services.AddDbContextFactory<MediaLinkCacheDbContext>(
                opts => opts.UseSqlite( settings.LinkCacheConnectionString )
            );
            ConfigureSwagger( services );

            // Optional ATProto storage and cache services
            ConfigureATProtoIfEnabled( services, settings );

            // Provider registrations (Apple/Spotify/Tidal)
            HashSet<SupportedProviders> enabledProviders = RegisterMusicProviders( services, settings );

            // Validate at least one provider
            if (enabledProviders.Count == 0) {
                throw new InvalidOperationException( "Required settings are missing. Cannot add TuneBridge services if no IMusicLookupService(s) are available." );
            }

            // MediaLink services (base + optional cache wrapper)
            RegisterMediaLinkService( services, enabledProviders, settings );
            _ = services.AddSingleton( enabledProviders );

            // Discord configuration
            _ = services.AddTransient( s => new DiscordNodeConfig( s.GetRequiredService<IMediaLinkService>( ), settings.NodeNumber ) );
            ConfigureDiscordIfEnabled( services, settings );

            return services;
        }

        /// <summary>
        /// Registers TuneBridge services, authentication handlers, HTTP clients, and (optional) Discord services.
        /// </summary>
        /// <param name="builder">The builder to create a web application from.</param>
        /// <returns>The configured application.</returns>
        public static async Task<WebApplication> ConfigureTuneBridgeAsync(
            this WebApplicationBuilder builder
        ) {
            WebApplication app = builder.Build();

            IConfiguration config = app.Configuration;

            AppSettings settings = new( );
            config.GetRequiredSection( "TuneBridge" ).Bind( settings );

            // Initialize cache database if configured
            InitializeCacheDatabase( app.Services );

            // Restrict Swagger UI access to authenticated users
            _ = app.UseMiddleware<SwaggerAuthorizationMiddleware>( );

            // Enable Swagger middleware
            _ = app.UseSwagger( );
            _ = app.UseSwaggerUI( options => {
                options.SwaggerEndpoint( "/swagger/v1/swagger.json", "TuneBridge API v1" );
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "TuneBridge API Documentation";
            } );

            return app;
        }


        /// <summary>
        /// Initializes the SQLite database for caching if ATProto PDS is configured.
        /// </summary>
        /// <param name="serviceProvider">The service provider to use for resolving services.</param>
        private static void InitializeCacheDatabase( IServiceProvider serviceProvider ) {
            try {
                // Create a scope to resolve services properly
                using IServiceScope scope = serviceProvider.CreateScope( );
                IDbContextFactory<MediaLinkCacheDbContext>? factory = scope.ServiceProvider.GetService<IDbContextFactory<MediaLinkCacheDbContext>>( );
                if (factory is null) { return; }

                using MediaLinkCacheDbContext dbContext = factory.CreateDbContext( );
                dbContext.Database.Migrate( );
                Microsoft.Extensions.Logging.ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "TuneBridge.Configuration.StartupExtensions" );
                logger.LogInformation( "TuneBridge: SQLite cache database initialized successfully" );
            } catch (Exception ex) {
                Microsoft.Extensions.Logging.ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "TuneBridge.Configuration.StartupExtensions" );
                logger.LogError( ex, "Failed to initialize SQLite cache database" );
            }
        }

        /// <summary>
        /// Adds a standard http client resilience pipeline to the builder, configuring retry and timeout policies.
        /// </summary>
        /// <param name="builder">The HTTP client builder to configure.</param>
        /// <returns>The configured <see cref="IHttpStandardResiliencePipelineBuilder"/>.</returns>
        private static IHttpStandardResiliencePipelineBuilder AddStandardResilience( this IHttpClientBuilder builder ) {
            return builder.AddStandardResilienceHandler( options => {
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.MaxRetryAttempts = 5;
                options.Retry.Delay = TimeSpan.FromSeconds( 1 );
                options.Retry.MaxDelay = TimeSpan.FromSeconds( 60 ); // Increased from 30 to 60 seconds
                options.Retry.ShouldRetryAfterHeader = true; // honor Retry-After

                options.Retry.DisableForUnsafeHttpMethods( ); // Disables retry on POST/PUT/PATCH/DELETE/CONNECT

                // Timeouts (outer total, inner per-attempt)
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds( 120 );
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds( 10 );
            } );
        }

        private static void ConfigureSwagger( IServiceCollection services ) {
            _ = services.AddEndpointsApiExplorer( );
            _ = services.AddSwaggerGen( options => {
                options.SwaggerDoc( "v1", new OpenApiInfo {
                    Title = "TuneBridge API",
                    Version = "v1",
                    Description = "Cross-platform music link converter and lookup service for Apple Music, Spotify, and Tidal. Convert music links between platforms, search by URL, ISRC, UPC, or title/artist.",
                    Contact = new OpenApiContact {
                        Name = "TuneBridge",
                        Url = new Uri( "https://github.com/tsmarvin/TuneBridge" )
                    },
                    License = new OpenApiLicense {
                        Name = "MIT License",
                        Url = new Uri( "https://github.com/tsmarvin/TuneBridge/blob/main/LICENSE" )
                    }
                } );
                options.AddSecurityDefinition( "ApiKey", new OpenApiSecurityScheme {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-API-Key",
                    Description = "API Key authentication. Get your API key by registering at /account/register"
                } );
                options.AddSecurityRequirement( ( d ) => new OpenApiSecurityRequirement { [new( "X-API-Key" )] = [] } );

                // Include XML comments from all assemblies if available
                foreach (string xmlPath in Directory.GetFiles( AppContext.BaseDirectory, "*.xml" )) {
                    options.IncludeXmlComments( xmlPath );
                }
            } );
        }

        private static void ConfigureATProtoIfEnabled( IServiceCollection services, AppSettings settings ) {
            if (string.IsNullOrWhiteSpace( settings.ATProtoIdentifier ) ||
                string.IsNullOrWhiteSpace( settings.ATProtoPassword )) {
                return;
            }

            _ = services.AddSingleton<IATProtoStorageService>( s =>
                new ATProtoStorageService(
                    settings.ATProtoIdentifier,
                    settings.ATProtoPassword,
                    s.GetRequiredService<ILogger<ATProtoStorageService>>( )
                )
            );

            // Register cache service as singleton using DbContextFactory so it is root-safe
            _ = services.AddSingleton<IMediaLinkCacheRepository>( s => new MediaLinkCacheRepository(
                s.GetRequiredService<IDbContextFactory<MediaLinkCacheDbContext>>( ),
                s.GetRequiredService<IATProtoStorageService>( ),
                s.GetRequiredService<ILogger<MediaLinkCacheRepository>>( ),
                settings.CacheDays
            ) );
        }

        private static HashSet<SupportedProviders> RegisterMusicProviders( IServiceCollection services, AppSettings settings ) {
            HashSet<SupportedProviders> enabledProviders = [ ];

            RegisterAppleIfConfigured( services, settings, enabledProviders );
            RegisterSpotifyIfConfigured( services, settings, enabledProviders );
            RegisterTidalIfConfigured( services, settings, enabledProviders );

            return enabledProviders;
        }

        private static void RegisterAppleIfConfigured( IServiceCollection services, AppSettings settings, HashSet<SupportedProviders> enabledProviders ) {
            // Check if Apple Music credentials are provided
            if (string.IsNullOrWhiteSpace( settings.AppleTeamId ) ||
                string.IsNullOrWhiteSpace( settings.AppleKeyId )) {
                return;
            }

            // If Team ID and Key ID are provided, the key path must also be provided and valid
            if (string.IsNullOrWhiteSpace( settings.AppleKeyPath )) {
                return;
            }

            // Fail fast if missing required apple key file.
            FileInfo keyPath = new( settings.AppleKeyPath );
            if (!keyPath.Exists) {
                throw new FileNotFoundException( $"Missing .p8 file at: {keyPath.FullName}" );
            }

            // Fail fast if apple key file is empty.
            string keyContents = File.ReadAllText( keyPath.FullName );
            if (string.IsNullOrWhiteSpace( keyContents )) {
                throw new InvalidDataException( $".p8 file missing contents at: {keyPath.FullName}" );
            }

            _ = services.AddHttpClient( "musickit-api", c => {
                c.BaseAddress = new Uri( "https://api.music.apple.com/v1/catalog/" );
            } ).AddStandardResilience( );

            _ = services.AddSingleton( new AppleJwtHandler( settings.AppleTeamId, settings.AppleKeyId, keyContents ) );
            _ = services.AddTransient<AppleMusicLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.AppleMusic );
        }

        private static void RegisterSpotifyIfConfigured( IServiceCollection services, AppSettings settings, HashSet<SupportedProviders> enabledProviders ) {
            if (string.IsNullOrWhiteSpace( settings.SpotifyClientId ) ||
                string.IsNullOrWhiteSpace( settings.SpotifyClientSecret )) {
                return;
            }

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
        }

        private static void RegisterTidalIfConfigured(
            IServiceCollection services,
            AppSettings settings,
            HashSet<SupportedProviders> enabledProviders
        ) {
            if (string.IsNullOrWhiteSpace( settings.TidalClientId ) ||
                string.IsNullOrWhiteSpace( settings.TidalClientSecret )) {
                return;
            }

            _ = services.AddHttpClient( "tidal-auth", c => {
                c.BaseAddress = new Uri( "https://auth.tidal.com/" );
            } ).AddStandardResilience( );

            _ = services.AddHttpClient( "tidal-api", c => {
                c.BaseAddress = new Uri( "https://openapi.tidal.com/v2/" );
            } ).AddStandardResilience( );

            _ = services.AddSingleton( new TidalCredentials( settings.TidalClientId, settings.TidalClientSecret ) );
            _ = services.AddTransient<TidalTokenHandler>( );
            _ = services.AddTransient<TidalLookupService>( );

            _ = enabledProviders.Add( SupportedProviders.Tidal );
        }

        private static void RegisterMediaLinkService(
            IServiceCollection services,
            HashSet<SupportedProviders> enabledProviders,
            AppSettings settings
        ) {
            _ = services.AddTransient<IMediaLinkService>( s => {
                Dictionary<SupportedProviders, IMusicLookupService> providerServices = GetEnabledProviderServices( enabledProviders, s );

                // Use caching service if ATProto is configured, otherwise use default service
                return !string.IsNullOrWhiteSpace( settings.ATProtoIdentifier ) &&
                    !string.IsNullOrWhiteSpace( settings.ATProtoPassword )
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
        }

        private static void ConfigureDiscordIfEnabled(
            IServiceCollection services,
            AppSettings settings
        ) {
            // Only register Discord services if token is provided and not empty/whitespace
            if (string.IsNullOrWhiteSpace( settings.DiscordToken )) { return; }

            _ = services.AddDiscordShardedGateway( options => {
                options.Token = settings.DiscordToken;
                options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
            } );
            _ = services.AddShardedGatewayHandlers( typeof( StartupExtensions ).Assembly );
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
            Dictionary<SupportedProviders, IMusicLookupService> results = [ ];
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

    }
}
