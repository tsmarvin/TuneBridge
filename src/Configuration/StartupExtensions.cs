using System.Text.Json;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.OpenApi;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using Polly;
using Serilog;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Implementations.Database;
using TuneBridge.Domain.Implementations.Middleware;
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
                _ = builder.Configuration
                .ConfigureAppSettings( args );

                // Configure logging after configuration is loaded
                ConfigureSerilog( builder );
                ConfigureOpenTelemetry( builder );
            }

            IServiceCollection services = builder.Services;
            IConfiguration config = builder.Configuration;

            _ = builder.WebHost.ConfigureTuneBridgeServices(
                services,
                config
            );

            return builder;
        }

        internal static TBuilder ConfigureTuneBridgeServices<TBuilder>(
            this TBuilder builder,
            IServiceCollection services,
            IConfiguration config
        ) where TBuilder : IWebHostBuilder {
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
        internal static IServiceCollection AddTuneBridgeServices(
            this IServiceCollection services,
            IConfiguration config
        ) {
            // Add services to the container.
            _ = services
                .AddControllersWithViews( )
                .AddRazorOptions( o => {
                    o.ViewLocationFormats.Clear( );
                    o.ViewLocationFormats.Add( "/Web/Views/{1}/{0}.cshtml" );
                    o.ViewLocationFormats.Add( "/Web/Views/Shared/{0}.cshtml" );
                } );

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
            _ = services.AddSingleton<IOpenGraphCardService, OpenGraphCardService>(
                p => new OpenGraphCardService( settings.BaseUrl )
            );

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

            // Initialize database and seed roles
            await app.InitializeDatabaseAsync( );

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment( )) {
                _ = app.UseExceptionHandler( "/Home/Error" );
                _ = app.UseHsts( );
            }

            _ = app.UseHttpsRedirection( );

            // Configure static file provider with proper MIME types for AT Protocol lexicon files
            FileExtensionContentTypeProvider provider = new( );
            provider.Mappings[".json"] = "application/json";

            // Serve .well-known directory with proper content types for AT Protocol lexicons
            _ = app.UseStaticFiles( new StaticFileOptions {
                FileProvider = new PhysicalFileProvider( Path.Combine( builder.Environment.WebRootPath, ".well-known" ) ),
                RequestPath = "/.well-known",
                ContentTypeProvider = provider,
                ServeUnknownFileTypes = false,
                OnPrepareResponse = ctx => {
                    // Add CORS headers to allow ATProto PDS to fetch lexicon files
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Origin", "*" );
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Methods", "GET, HEAD, OPTIONS" );
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Headers", "Content-Type" );

                    // Cache lexicon files for 1 day
                    ctx.Context.Response.Headers.Append( "Cache-Control", "public, max-age=86400" );
                }
            } );

            _ = app.UseStaticFiles( ); // Serve static files from wwwroot
            _ = app.UseRouting( );

            // Restrict health endpoint access to internal requests only
            // NOTE: This middleware is intentionally placed before authentication because it uses IP-based authorization
            // and does not require authenticated user context. If future changes require authentication, adjust the order accordingly.
            _ = app.UseMiddleware<HealthEndpointAuthorizationMiddleware>( );

            _ = app.UseAuthentication( );
            _ = app.UseAuthorization( );

            // Restrict Swagger UI access to authenticated users
            _ = app.UseMiddleware<SwaggerAuthorizationMiddleware>( );

            // Add rate limiting middleware with configured rate limit
            _ = app.UseMiddleware<RateLimitingMiddleware>( settings.RateLimitRequestsPerHour );

            // Enable Swagger middleware
            _ = app.UseSwagger( );
            _ = app.UseSwaggerUI( options => {
                options.SwaggerEndpoint( "/swagger/v1/swagger.json", "TuneBridge API v1" );
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "TuneBridge API Documentation";
            } );

            _ = app.MapStaticAssets( );
            _ = app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}" )
                .WithStaticAssets( );

            return app;
        }

        /// <summary>
        /// Configures the application settings by adding command line arguments, environment variables, and the appsettings.json file.
        /// </summary>
        /// <param name="config">The configuration builder to extend.</param>
        /// <param name="args">Command line arguments passed to the application.</param>
        /// <returns>The updated <see cref="IConfigurationBuilder"/>.</returns>
        private static IConfigurationBuilder ConfigureAppSettings(
            this IConfigurationBuilder config,
            string[] args
        ) => config.AddJsonFile(
                    path: "appsettings.json",
                    optional: false,
                    reloadOnChange: false
                ).AddCommandLine( args )
                .AddEnvironmentVariables( );

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

        private static void ConfigureDatabases( IServiceCollection services, AppSettings settings ) {
            _ = services.AddDbContext<ApplicationDbContext>( options =>
                options.UseSqlite( settings.IdentityConnectionString )
            );

            // Register a factory for MediaLinkCacheDbContext to be consumed from singleton services safely
            _ = services.AddDbContextFactory<MediaLinkCacheDbContext>(
                opts => opts.UseSqlite( settings.LinkCacheConnectionString )
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
                options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey( "X-API-Key" )
                    ? ApiKeyDefaults.AuthenticationScheme
                    : IdentityConstants.ApplicationScheme;
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
            if (string.IsNullOrWhiteSpace( settings.BlueskyPdsUrl ) ||
                string.IsNullOrWhiteSpace( settings.BlueskyIdentifier ) ||
                string.IsNullOrWhiteSpace( settings.BlueskyPassword )) {
                return;
            }

            _ = services.AddSingleton<IATProtoStorageService>( s =>
                new ATProtoStorageService(
                    settings.BlueskyPdsUrl,
                    settings.BlueskyIdentifier,
                    settings.BlueskyPassword,
                    s.GetRequiredService<ILogger<ATProtoStorageService>>( )
                )
            );

            // Register cache service as singleton using DbContextFactory so it is root-safe
            _ = services.AddSingleton<IMediaLinkCacheService>( s => new MediaLinkCacheService(
                s.GetRequiredService<IDbContextFactory<MediaLinkCacheDbContext>>( ),
                s.GetRequiredService<IATProtoStorageService>( ),
                s.GetRequiredService<ILogger<MediaLinkCacheService>>( ),
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
                DefaultMediaLinkService baseService = new(
                    GetEnabledProviderServices( enabledProviders, s ),
                    s.GetRequiredService<ILogger<DefaultMediaLinkService>>( ),
                    s.GetRequiredService<JsonSerializerOptions>( )
                );

                // Wrap with caching if ATProto is configured
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
            _ = services.AddShardedGatewayHandlers( typeof( Program ).Assembly );
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

        /// <summary>
        /// Configures Serilog for file logging with rotation and retention.
        /// </summary>
        /// <param name="builder">The web application builder to configure.</param>
        private static void ConfigureSerilog( WebApplicationBuilder builder ) {
            string logPath = builder.Configuration["Logging:FilePath"] ?? "/app/data/logs/tunebridge-.log";

            try {
                string? logDir = Path.GetDirectoryName( logPath );
                if (!string.IsNullOrEmpty( logDir ) && !Directory.Exists( logDir )) {
                    Directory.CreateDirectory( logDir );
                }
            } catch (Exception ex) {
                Console.WriteLine( $"Warning: Failed to validate/create log directory: {ex.Message}" );
                // Fall back to not configuring file logging
                return;
            }

            Log.Logger = new LoggerConfiguration( )
                .ReadFrom.Configuration( builder.Configuration )
                .Filter.ByExcluding( logEvent => {
                    // Exclude successful health check requests from logs (but keep failures)
                    // This filters out Information level logs for GET /health with 200 OK status
                    if (logEvent.Level != Serilog.Events.LogEventLevel.Information) {
                        return false; // Don't exclude warnings, errors, etc. (failures will be at Warning or Error level)
                    }

                    // Check if this is an HTTP request completion log from Serilog.AspNetCore
                    // The message template for HTTP request completion is typically:
                    // "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms"
                    string? messageTemplate = logEvent.MessageTemplate?.Text;
                    if (messageTemplate == null || !messageTemplate.Contains( "HTTP" )) {
                        return false;
                    }

                    // Check if request path is /health
                    if (logEvent.Properties.TryGetValue( "RequestPath", out Serilog.Events.LogEventPropertyValue? pathValue )) {
                        string path = pathValue.ToString( ).Trim( '"' );
                        if (path.Equals( "/health", StringComparison.OrdinalIgnoreCase )) {
                            // Check if status code is 200 (success)
                            if (logEvent.Properties.TryGetValue( "StatusCode", out Serilog.Events.LogEventPropertyValue? statusValue )) {
                                string status = statusValue.ToString( );
                                if (status == "200") {
                                    return true; // Exclude this successful health check log
                                }
                            }
                        }
                    }

                    return false;
                } )
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
                    retainedFileCountLimit: 5,
                    rollOnFileSizeLimit: true,
                    shared: false
                )
                .CreateLogger( );

            _ = builder.Host.UseSerilog( );
        }

        /// <summary>
        /// Configures OpenTelemetry for OTLP export to Aspire Dashboard.
        /// </summary>
        /// <param name="builder">The web application builder to configure.</param>
        private static void ConfigureOpenTelemetry( WebApplicationBuilder builder ) {
            string? otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];

            // Only configure OpenTelemetry if endpoint is provided
            if (string.IsNullOrWhiteSpace( otlpEndpoint )) {
                return;
            }

            if (!Uri.TryCreate( otlpEndpoint, UriKind.Absolute, out Uri? uri )) {
                Console.WriteLine( $"Warning: Invalid OTLP endpoint URL '{otlpEndpoint}' - OpenTelemetry logging disabled" );
                return;
            }

            var version = typeof( Program ).Assembly.GetName( ).Version?.ToString( ) ?? "0.0.1";
            _ = builder.Logging.AddOpenTelemetry( options => {
                options.SetResourceBuilder(
                    ResourceBuilder.CreateDefault( )
                        .AddService( serviceName: "TuneBridge", serviceVersion: version )
                );

                options.AddOtlpExporter( otlpOptions => {
                    otlpOptions.Endpoint = uri;
                } );
            } );
        }
    }
}
