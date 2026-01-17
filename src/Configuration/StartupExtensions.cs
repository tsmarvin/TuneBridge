using System.Security.Cryptography;
using System.Text.Json;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Domain.Implementations.Middleware;
using BridgeBeats.Domain.Implementations.Services;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Identity;
using BridgeBeats.Infrastructure.Storage;
using BridgeBeats.Providers;
using BridgeBeats.ServiceDefaults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace BridgeBeats.Configuration {

    /// <summary>
    /// Extension methods for configuring the BridgeBeats services and HTTP client resilience (retry) policies.
    /// </summary>
    internal static class StartupExtensions {

        /// <summary>
        /// Registers BridgeBeats services, authentication handlers, HTTP clients, and (optional) Discord services.
        /// </summary>
        /// <param name="builder">The builder to configure.</param>
        /// <param name="args">The commandline arguments.</param>
        /// <returns>The configured builder.</returns>
        public static WebApplicationBuilder ConfigureBridgeBeatsServices(
            this WebApplicationBuilder builder,
            string[] args
        ) {
            // Always load configuration (appsettings.json, user secrets, env vars)
            // Tests can override via environment variables or WebApplicationFactory hooks
            _ = builder.Configuration.ConfigureAppSettings( args );

            // Configure logging only in non-Testing environments
            if (builder.Environment.EnvironmentName != "Testing") {
                ConfigureSerilog( builder );
            }

            _ = builder.AddServiceDefaults( );

            _ = builder.WebHost.ConfigureBridgeBeatsServices(
                builder.Services,
                builder.Configuration,
                builder.Environment.EnvironmentName
            );

            return builder;
        }

        internal static TBuilder ConfigureBridgeBeatsServices<TBuilder>(
            this TBuilder builder,
            IServiceCollection services,
            IConfiguration config,
            string environment
        ) where TBuilder : IWebHostBuilder {
            _ = AddBridgeBeatsServices( services, config, environment );
            return builder;
        }

        /// <summary>
        /// Registers BridgeBeats services, authentication handlers, HTTP clients, and (optional) Discord services directly on an IServiceCollection.
        /// This is useful for testing scenarios where you don't need a full IWebHostBuilder.
        /// </summary>
        /// <param name="services">The service collection to configure.</param>
        /// <param name="config">The configuration to use for settings.</param>
        /// <param name="environment">The environment name.</param>
        /// <returns>The configured service collection.</returns>
        internal static IServiceCollection AddBridgeBeatsServices(
            this IServiceCollection services,
            IConfiguration config,
            string environment
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
            config.GetRequiredSection( "BridgeBeats" ).Bind( settings );

            // Common singletons and caching
            _ = services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );
            _ = services.AddMemoryCache( );

            ConfigureDatabases( services, settings );
            ConfigureIdentity( services );
            ConfigureApiKeyAuth( services, settings );
            ConfigureSwagger( services );

            // Validate card cache settings
            if (settings.CardCacheExpirationHours <= 0) {
                throw new InvalidOperationException( $"CardCacheExpirationHours must be greater than zero. Current value: {settings.CardCacheExpirationHours}" );
            }
            if (settings.CardCacheCleanupInterval <= 0) {
                throw new InvalidOperationException( $"CardCacheCleanupInterval must be greater than zero. Current value: {settings.CardCacheCleanupInterval}" );
            }

            // Misc domain services
            _ = services.AddSingleton<IOpenGraphCardService, OpenGraphCardService>(
                _ => new OpenGraphCardService(
                    settings.BaseUrl,
                    settings.CardCacheExpirationHours,
                    settings.CardCacheCleanupInterval
                )
            );

            // Playlist service (singleton with DbContextFactory for thread-safe database access)
            _ = services.AddSingleton<IPlaylistService, PlaylistService>(
                p => new PlaylistService( settings.BaseUrl, p.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( ) )
            );

            // Register playlist cleanup background service
            _ = services.AddHostedService<PlaylistCleanupService>( );

            // Optional ATProto storage and cache services
            ConfigureATProtoIfEnabled( services, settings );

            // Provider registrations (Apple/Spotify/Tidal)
            HashSet<SupportedProviders> enabledProviders = RegisterMusicProviders( services, settings );

            // Validate at least one provider
            if (enabledProviders.Count == 0) {
                throw new InvalidOperationException( "Required settings are missing. Cannot add BridgeBeats services if no IMusicLookupService(s) are available." );
            }

            // MediaLink services (base + optional cache wrapper)
            RegisterMediaLinkService( services, enabledProviders, settings );
            _ = services.AddSingleton( enabledProviders );

            // Discord configuration
            _ = services.AddTransient( s => new DiscordNodeConfig( s.GetRequiredService<IMediaLinkService>( ), settings.NodeNumber ) );
            ConfigureDiscordIfEnabled( services, settings, environment );

            return services;
        }

        /// <summary>
        /// Registers BridgeBeats services, authentication handlers, HTTP clients, and (optional) Discord services.
        /// </summary>
        /// <param name="builder">The builder to create a web application from.</param>
        /// <returns>The configured application.</returns>
        public static async Task<WebApplication> ConfigureBridgeBeatsAsync(
            this WebApplicationBuilder builder
        ) {
            WebApplication app = builder.Build();

            IConfiguration config = app.Configuration;

            AppSettings settings = new( );
            config.GetRequiredSection( "BridgeBeats" ).Bind( settings );

            // Skip database initialization in Testing environment - tests will configure their own databases
            // via WebApplicationFactory.ConfigureServices which replaces the DbContext factories
            if (app.Environment.EnvironmentName != "Testing") {
                // Initialize cache database if configured
                InitializeCacheDatabase( app.Services );

                // Initialize database and seed roles
                _ = await app.InitializeDatabaseAsync( );
            }

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
                ServeUnknownFileTypes = true, // Allow serving files without extensions (required for ATProto lexicons)
                DefaultContentType = "application/octet-stream", // Default for unknown types
                OnPrepareResponse = ctx => {
                    // Serve lexicon files in atproto-lexicon directory as application/json
                    if (ctx.File.PhysicalPath != null && ctx.File.PhysicalPath.Contains( "atproto-lexicon", StringComparison.OrdinalIgnoreCase )) {
                        ctx.Context.Response.ContentType = "application/json; charset=utf-8";
                    }

                    // Add CORS headers to allow ATProto PDS to fetch lexicon files
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Origin", "*" );
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Methods", "GET, HEAD, OPTIONS" );
                    ctx.Context.Response.Headers.Append( "Access-Control-Allow-Headers", "Content-Type" );

                    // Cache lexicon files for 1 day
                    ctx.Context.Response.Headers.Append( "Cache-Control", "public, max-age=86400" );
                }
            } );

            // CSP middleware: adds Content-Security-Policy header. Relax frame-ancestors for embed endpoints.
            // Must be placed before the main UseStaticFiles() call (see line 227) to ensure CSP headers are applied to those responses.
            _ = app.Use( async ( ctx, next ) => {
                string path = ctx.Request.Path.Value ?? string.Empty;
                bool isEmbed = path.EndsWith( "/embed", StringComparison.OrdinalIgnoreCase ) && (path.StartsWith( "/card/", StringComparison.OrdinalIgnoreCase ) || path.StartsWith( "/playlist/", StringComparison.OrdinalIgnoreCase ));
                bool isAppleMusic = path.StartsWith( "/applemusic", StringComparison.OrdinalIgnoreCase );

                // Generate a unique nonce for this request to allow inline scripts and styles
                string nonce = Convert.ToBase64String( RandomNumberGenerator.GetBytes( 16 ) );
                ctx.Items["CSPNonce"] = nonce;

                // Build CSP policy
                // Allow MusicKit JS CDN, Cloudflare analytics, nonce-based inline styles, and API calls to music providers
                // Allow framing for embed endpoints, Apple Music integration, and playlist pages
                // Note: frame-ancestors uses https: scheme with 'self' to support HTTPS framing including same-origin
                string frameAncestors = (isEmbed || isAppleMusic) ? "https: 'self'" : "'self'";

                string csp = string.Join( "; ", new[] {
                    "default-src 'self'",
                    $"script-src 'self' 'nonce-{nonce}' https://js-cdn.music.apple.com https://static.cloudflareinsights.com",
                    $"style-src 'self' 'nonce-{nonce}'",
                    "img-src 'self' data: https:",
                    "font-src 'self' data:",
                    "connect-src 'self' https://api.music.apple.com https://accounts.spotify.com https://api.spotify.com https://openapi.tidal.com https://cloudflareinsights.com",
                    "media-src 'self' https:",
                    "frame-ancestors " + frameAncestors,
                    "object-src 'none'",
                    "base-uri 'self'",
                    "form-action 'self'"
                } );

                _ = ctx.Response.Headers.Remove( "Content-Security-Policy" );
                ctx.Response.Headers.Append( "Content-Security-Policy", csp );

                await next( );
            } );

            _ = app.UseStaticFiles( ); // Serve static files from wwwroot
            _ = app.UseRouting( );

            // Restrict liveness endpoint access to internal requests only
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
                options.SwaggerEndpoint( "/swagger/v1/swagger.json", "BridgeBeats API v1" );
                options.RoutePrefix = "swagger";
                options.DocumentTitle = "BridgeBeats API Documentation";
            } );

            _ = app.MapDefaultEndpoints( );
            _ = app.MapStaticAssets( );
            _ = app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}" )
                .WithStaticAssets( );

            return app;
        }

        /// <summary>
        /// Configures the application settings by adding command line arguments, environment variables, user secrets, and the appsettings.json file.
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
                ).AddUserSecrets<Program>( optional: true )
                .AddCommandLine( args )
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
                Microsoft.Extensions.Logging.ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "BridgeBeats.Configuration.StartupExtensions" );
                logger.LogInformation( "BridgeBeats: SQLite cache database initialized successfully" );
            } catch (Exception ex) {
                Microsoft.Extensions.Logging.ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "BridgeBeats.Configuration.StartupExtensions" );
                logger.LogError( ex, "Failed to initialize SQLite cache database" );
            }
        }

        private static void ConfigureDatabases( IServiceCollection services, AppSettings settings ) {
            // Register DbContext factory for on-demand instance creation
            // Controllers and services will use IDbContextFactory<ApplicationDbContext> to create scoped instances when needed
            _ = services.AddDbContextFactory<ApplicationDbContext>( options =>
                options.UseSqlite(
                    settings.IdentityConnectionString,
                    b => b.MigrationsAssembly( "BridgeBeats.Infrastructure" )
                )
            );

            // Register a factory for MediaLinkCacheDbContext to be consumed from singleton services safely
            _ = services.AddDbContextFactory<MediaLinkCacheDbContext>( options =>
                options.UseSqlite(
                    settings.LinkCacheConnectionString,
                    b => b.MigrationsAssembly( "BridgeBeats.Infrastructure" )
                )
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

            // Register scoped ApplicationDbContext for Identity framework using the factory
            _ = services.AddScoped( sp => {
                IDbContextFactory<ApplicationDbContext> factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
                return factory.CreateDbContext( );
            } );
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
                options.Realm = "BridgeBeats API";
                options.KeyName = "X-API-Key";
            } )
            .AddIdentityCookies( ); // Add cookie authentication for web UI

            _ = services.AddAuthorization( );
        }

        private static void ConfigureSwagger( IServiceCollection services ) {
            _ = services.AddEndpointsApiExplorer( );
            _ = services.AddSwaggerGen( options => {
                options.SwaggerDoc( "v1", new OpenApiInfo {
                    Title = "BridgeBeats API",
                    Version = "v1",
                    Description = "Cross-platform music link converter and lookup service for Apple Music, Spotify, and Tidal. Convert music links between platforms, search by URL, ISRC, UPC, or title/artist.",
                    Contact = new OpenApiContact {
                        Name = "BridgeBeats",
                        Url = new Uri( "https://github.com/tsmarvin/BridgeBeats" )
                    },
                    License = new OpenApiLicense {
                        Name = "MIT License",
                        Url = new Uri( "https://github.com/tsmarvin/BridgeBeats/blob/main/LICENSE" )
                    }
                } );
                options.AddSecurityDefinition( "ApiKey", new OpenApiSecurityScheme {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = "X-API-Key",
                    Description = "API Key authentication. Get your API key by registering at /account/register"
                } );
                options.AddSecurityRequirement( _ => new OpenApiSecurityRequirement { [new( "X-API-Key" )] = [] } );

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
                settings.CacheDays,
                settings.ATProtoUserDID
            ) );
        }

        private static HashSet<SupportedProviders> RegisterMusicProviders( IServiceCollection services, AppSettings settings ) {
            return services.AddMusicProviders(
                settings.AppleTeamId,
                settings.AppleKeyId,
                settings.AppleKeyPath,
                settings.SpotifyClientId,
                settings.SpotifyClientSecret,
                settings.TidalClientId,
                settings.TidalClientSecret
            );
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
            AppSettings settings,
            string environment
        ) {
            if (environment == "Testing") { return; }

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
                        results.Add( SupportedProviders.AppleMusic, serviceProvider.GetRequiredService<Providers.AppleMusic.AppleMusicLookupService>( ) );
                        break;
                    case SupportedProviders.Spotify:
                        results.Add( SupportedProviders.Spotify, serviceProvider.GetRequiredService<Providers.Spotify.SpotifyLookupService>( ) );
                        break;
                    case SupportedProviders.Tidal:
                        results.Add( SupportedProviders.Tidal, serviceProvider.GetRequiredService<Providers.Tidal.TidalLookupService>( ) );
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
            string logPath = builder.Configuration["BridgeBeats:LogFilePath"] ?? "./logs/bridgebeats-.log";

            try {
                string? logDir = Path.GetDirectoryName( logPath );
                if (!string.IsNullOrEmpty( logDir ) && !Directory.Exists( logDir )) {
                    _ = Directory.CreateDirectory( logDir );
                }
            } catch (IOException ex) {
                Console.WriteLine( $"Warning: Failed to validate/create log directory: {ex.Message}" );
                // Fall back to not configuring file logging
                return;
            } catch (UnauthorizedAccessException ex) {
                Console.WriteLine( $"Warning: Failed to validate/create log directory due to insufficient permissions: {ex.Message}" );
                // Fall back to not configuring file logging
                return;
            }

            Log.Logger = new LoggerConfiguration( )
                .ReadFrom.Configuration( builder.Configuration )
                .Filter.ByExcluding( logEvent => {
                    // Exclude successful health check requests from logs (but keep failures)
                    // This filters out Information level logs for /health endpoint
                    if (logEvent.Level != Serilog.Events.LogEventLevel.Information) {
                        return false; // Don't exclude warnings, errors, etc. (allows failures and higher log levels to pass through)
                    }

                    // Check if this is a log event related to health endpoint
                    // This covers both HTTP request logs from Serilog.AspNetCore and MVC action execution logs
                    string? sourceContext = logEvent.Properties.TryGetValue( "SourceContext", out Serilog.Events.LogEventPropertyValue? sourceValue )
                        ? sourceValue.ToString( ).Trim( '"' )
                        : null;

                    // Filter MVC controller action execution logs for health endpoint
                    // Check if this is an MVC/Routing infrastructure log
                    if (sourceContext != null &&
                        (sourceContext.Contains( "Microsoft.AspNetCore.Mvc" ) ||
                         sourceContext.Contains( "Microsoft.AspNetCore.Routing" ))) {

                        // Check ActionName property first (most reliable indicator)
                        if (logEvent.Properties.TryGetValue( "ActionName", out Serilog.Events.LogEventPropertyValue? actionValue )) {
                            string actionName = actionValue.ToString( );
                            if (actionName.Contains( "Health", StringComparison.OrdinalIgnoreCase )) {
                                return true; // Exclude health check related MVC logs
                            }
                        }

                        // Also check message text for "Health" keyword as fallback
                        // This catches logs like "Route matched with {action = "Health", controller = "Home"}"
                        string messageText = logEvent.RenderMessage( );
                        if (messageText.Contains( "Health", StringComparison.OrdinalIgnoreCase )) {
                            return true; // Exclude health check related MVC logs
                        }
                    }

                    // Filter HTTP request completion logs from Serilog.AspNetCore for successful health checks
                    if (logEvent.Properties.TryGetValue( "RequestPath", out Serilog.Events.LogEventPropertyValue? pathValue ) &&
                        (pathValue.ToString( ).Trim( '"' ).Equals( EndpointPaths.Health, StringComparison.OrdinalIgnoreCase ) ||
                         pathValue.ToString( ).Trim( '"' ).Equals( EndpointPaths.Alive, StringComparison.OrdinalIgnoreCase )) &&
                        logEvent.Properties.TryGetValue( "StatusCode", out Serilog.Events.LogEventPropertyValue? statusValue ) &&
                        statusValue.ToString( ) == "200") {
                        return true; // Exclude successful health check HTTP logs
                    }

                    return false;
                } )
                .WriteTo.Console( )
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

    }
}
