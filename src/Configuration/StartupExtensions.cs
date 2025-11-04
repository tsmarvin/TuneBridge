using System.Text.Json;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi.Models;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Polly;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Implementations.Services;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Models;
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
        /// <param name="environment">Optional environment name for loading environment-specific configuration.</param>
        /// <returns>The updated <see cref="IConfigurationBuilder"/>.</returns>
        public static IConfigurationBuilder ConfigureAppSettings(
            this IConfigurationBuilder config,
            string[] args,
            string? environment = null
        ) {
            _ = config.AddCommandLine( args )
                .AddEnvironmentVariables( )
                .AddJsonFile(
                    path: "appsettings.json",
                    optional: false,
                    reloadOnChange: true
                );

            // Add environment-specific appsettings if environment is specified
            if (!string.IsNullOrWhiteSpace( environment )) {
                _ = config.AddJsonFile(
                    path: $"appsettings.{environment}.json",
                    optional: true,
                    reloadOnChange: true
                );
            }

            return config;
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
            AppSettings settings = new( );
            config.GetRequiredSection( "TuneBridge" ).Bind( settings );

            // Common singletons and caching
            _ = services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );
            _ = services.AddMemoryCache( );

            ConfigureDatabases( services, settings );
            ConfigureIdentity( services );
            ConfigureApiKeyAuth( services, settings );
            ConfigureSwagger( services );

            // Misc domain services
            _ = services.AddSingleton<IOpenGraphCardService, OpenGraphCardService>( );

            // Optional Bluesky storage and cache services
            ConfigureBlueskyIfEnabled( services, settings );

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
        /// Initializes the SQLite database for caching if Bluesky PDS is configured.
        /// </summary>
        /// <param name="serviceProvider">The service provider to use for resolving services.</param>
        public static void InitializeCacheDatabase( IServiceProvider serviceProvider ) {
            try {
                // Create a scope to resolve services properly
                using IServiceScope scope = serviceProvider.CreateScope( );
                IDbContextFactory<MediaLinkCacheDbContext>? factory = scope.ServiceProvider.GetService<IDbContextFactory<MediaLinkCacheDbContext>>( );
                if (factory is not null) {
                    using MediaLinkCacheDbContext dbContext = factory.CreateDbContext( );
                    _ = dbContext.Database.EnsureCreated( );
                    ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "TuneBridge.Configuration.StartupExtensions" );
                    logger.LogInformation( "TuneBridge: SQLite cache database initialized successfully" );
                }
            } catch (Exception ex) {
                ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "TuneBridge.Configuration.StartupExtensions" );
                logger.LogError( ex, "Failed to initialize SQLite cache database" );
            }
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

        // ===== Private helpers =====

        private static void ConfigureDatabases( IServiceCollection services, AppSettings settings ) {
            _ = services.AddDbContext<ApplicationDbContext>( options =>
                options.UseSqlite( settings.ConnectionString )
            );

            // Register a factory for MediaLinkCacheDbContext to be consumed from singleton services safely
            _ = services.AddDbContextFactory<MediaLinkCacheDbContext>(
                opts => opts.UseSqlite( settings.CacheDbPath )
            );
        }

        private static void ConfigureIdentity( IServiceCollection services ) {
            _ = services.AddIdentityCore<ApplicationUser>( options => {
                // Password settings
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 14;

                // User settings
                options.User.RequireUniqueEmail = true;
            } )
            .AddRoles<IdentityRole>( )
            .AddEntityFrameworkStores<ApplicationDbContext>( )
            .AddSignInManager( )
            .AddDefaultTokenProviders( );
        }

        private static void ConfigureApiKeyAuth( IServiceCollection services, AppSettings settings ) {
            // Configure API Key hashing
            if (string.IsNullOrWhiteSpace( settings.ApiKeySalt )) {
                throw new InvalidOperationException( "ApiKeySalt is required in configuration for secure API key storage." );
            }
            _ = services.AddSingleton( new ApiKeyHasher( settings.ApiKeySalt ) );

            // Configure API Key authentication for API endpoints
            _ = services.AddTransient<IApiKeyProvider, ApiKeyProvider>( );
            _ = services.AddAuthentication( options => {
                options.DefaultScheme = "MultiScheme";
                options.DefaultChallengeScheme = "MultiScheme";
            } )
            .AddPolicyScheme( "MultiScheme", "API Key or Cookie", options => {
                options.ForwardDefaultSelector = context => {
                    // Use cookie authentication for web UI, API key for API endpoints
                    return context.Request.Headers.ContainsKey( "X-API-Key" )
                            ? ApiKeyDefaults.AuthenticationScheme
                            : IdentityConstants.ApplicationScheme;
                };
            } )
            .AddApiKeyInHeader<ApiKeyProvider>( options => {
                options.Realm = "TuneBridge API";
                options.KeyName = "X-API-Key";
            } )
            .AddIdentityCookies( ); // Add cookie authentication for web UI

            _ = services.AddAuthorization( );
        }

        private static void ConfigureSwagger( IServiceCollection services ) {
            _ = services.AddEndpointsApiExplorer( );
            _ = services.AddSwaggerGen( options => {
                options.SwaggerDoc( "v1", new OpenApiInfo {
                    Title = "TuneBridge API",
                    Version = "v1",
                    Description = "Cross-platform music link converter and lookup service for Apple Music, Spotify, and Tidal. Convert music links between platforms, search by URL, ISRC, UPC, or title/artist.",
                    Contact = new OpenApiContact {
                        Name = "Taylor Marvin",
                        Url = new Uri( "https://github.com/tsmarvin/TuneBridge" )
                    },
                    License = new OpenApiLicense {
                        Name = "MIT License",
                        Url = new Uri( "https://github.com/tsmarvin/TuneBridge/blob/main/LICENSE" )
                    }
                } );

                // Add API Key authentication to Swagger
                options.AddSecurityDefinition( "ApiKey", new OpenApiSecurityScheme {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-API-Key",
                    Description = "API Key authentication. Get your API key by registering at /account/register"
                } );

                options.AddSecurityRequirement( new OpenApiSecurityRequirement {
                    {
                        new OpenApiSecurityScheme {
                            Reference = new OpenApiReference {
                                Type = ReferenceType.SecurityScheme,
                                Id = "ApiKey"
                            }
                        },
                        Array.Empty<string>( )
                    }
                } );

                // Include XML comments from all assemblies if available
                foreach (string xmlPath in Directory.GetFiles( AppContext.BaseDirectory, "*.xml" )) {
                    options.IncludeXmlComments( xmlPath );
                }
            } );
        }

        private static void ConfigureBlueskyIfEnabled( IServiceCollection services, AppSettings settings ) {
            if (!string.IsNullOrWhiteSpace( settings.BlueskyPdsUrl ) &&
                !string.IsNullOrWhiteSpace( settings.BlueskyIdentifier ) &&
                !string.IsNullOrWhiteSpace( settings.BlueskyPassword )) {
                _ = services.AddSingleton<IBlueskyStorageService>( s =>
                    new BlueskyStorageService(
                        settings.BlueskyPdsUrl,
                        settings.BlueskyIdentifier,
                        settings.BlueskyPassword,
                        s.GetRequiredService<ILogger<BlueskyStorageService>>( )
                    )
                );

                // Register cache service as singleton using DbContextFactory so it is root-safe
                _ = services.AddSingleton<IMediaLinkCacheService>( s => new MediaLinkCacheService(
                    s.GetRequiredService<IDbContextFactory<MediaLinkCacheDbContext>>( ),
                    s.GetRequiredService<IBlueskyStorageService>( ),
                    s.GetRequiredService<ILogger<MediaLinkCacheService>>( ),
                    settings.CacheDays
                ) );
            }
        }

        private static HashSet<SupportedProviders> RegisterMusicProviders( IServiceCollection services, AppSettings settings ) {
            HashSet<SupportedProviders> enabledProviders = [ ];

            RegisterAppleIfConfigured( services, settings, enabledProviders );
            RegisterSpotifyIfConfigured( services, settings, enabledProviders );
            RegisterTidalIfConfigured( services, settings, enabledProviders );

            return enabledProviders;
        }

        private static void RegisterAppleIfConfigured( IServiceCollection services, AppSettings settings, HashSet<SupportedProviders> enabledProviders ) {
            if (string.IsNullOrWhiteSpace( settings.AppleTeamId ) == false &&
                string.IsNullOrWhiteSpace( settings.AppleKeyId ) == false) {

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
        }

        private static void RegisterSpotifyIfConfigured( IServiceCollection services, AppSettings settings, HashSet<SupportedProviders> enabledProviders ) {
            if (string.IsNullOrWhiteSpace( settings.SpotifyClientId ) == false &&
                string.IsNullOrWhiteSpace( settings.SpotifyClientSecret ) == false) {

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
        }

        private static void RegisterTidalIfConfigured( IServiceCollection services, AppSettings settings, HashSet<SupportedProviders> enabledProviders ) {
            if (string.IsNullOrWhiteSpace( settings.TidalClientId ) == false &&
                string.IsNullOrWhiteSpace( settings.TidalClientSecret ) == false) {

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
        }

        private static void RegisterMediaLinkService( IServiceCollection services, HashSet<SupportedProviders> enabledProviders, AppSettings settings ) {
            _ = services.AddTransient<IMediaLinkService>( s => {
                DefaultMediaLinkService baseService = new(
                    GetEnabledProviderServices( enabledProviders, s ),
                    s.GetRequiredService<ILogger<DefaultMediaLinkService>>( ),
                    s.GetRequiredService<JsonSerializerOptions>( )
                );

                // Wrap with caching if Bluesky is configured
                return !string.IsNullOrWhiteSpace( settings.BlueskyPdsUrl ) &&
                    !string.IsNullOrWhiteSpace( settings.BlueskyIdentifier ) &&
                    !string.IsNullOrWhiteSpace( settings.BlueskyPassword )
                    ? new CachedMediaLinkService(
                        baseService,
                        s.GetRequiredService<IMediaLinkCacheService>( ),
                        s.GetRequiredService<ILogger<CachedMediaLinkService>>( )
                    )
                    : baseService;
            } );
        }

        private static void ConfigureDiscordIfEnabled( IServiceCollection services, AppSettings settings ) {
            if (string.IsNullOrWhiteSpace( settings.DiscordToken ) == false) {
                _ = services.AddDiscordShardedGateway( options => {
                    options.Token = settings.DiscordToken;
                    options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
                } );
                _ = services.AddShardedGatewayHandlers( typeof( Program ).Assembly );
            }
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
