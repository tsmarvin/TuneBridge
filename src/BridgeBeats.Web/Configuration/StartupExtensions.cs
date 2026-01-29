using System.Security.Cryptography;
using System.Text.Json;
using AspNetCore.Authentication.ApiKey;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Services;
using BridgeBeats.Core.Infrastructure.Cache;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Middleware;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.OpenApi;
using StackExchange.Redis;

namespace BridgeBeats.Web.Configuration {

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
                _ = builder.ConfigureFileLogging( "Web" );
            }

            _ = builder.AddServiceDefaults( );

            // Register Redis client via Aspire (provides IConnectionMultiplexer)
            // In tests, CustomWebApplicationFactory provides the connection string via Testcontainers
            builder.AddRedisClient( "redis" );

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
                    o.ViewLocationFormats.Add( "/Views/{1}/{0}.cshtml" );
                    o.ViewLocationFormats.Add( "/Views/Shared/{0}.cshtml" );
                } );

            AppSettings settings = new( );
            config.GetRequiredSection( "BridgeBeats" ).Bind( settings );

            // Common singletons and caching
            _ = services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );
            _ = services.AddMemoryCache( );

            // Configure QueueSettings from configuration (with defaults)
            _ = services.Configure<QueueSettings>(
                config.GetSection( "BridgeBeats:Queue" )
            );

            ConfigureDatabases( services, settings );
            ConfigureIdentity( services );
            ConfigureApiKeyAuth( services, settings );
            ConfigureSwagger( services );

            // Optional ATProto storage and cache services
            ConfigureATProtoIfEnabled( services, settings );

            // Provider registrations (Apple/Spotify/Tidal)
            HashSet<SupportedProviders> enabledProviders = RegisterMusicProviders( services, settings );

            // Validate at least one provider
            if (enabledProviders.Count == 0) {
                throw new InvalidOperationException( "Required settings are missing. Cannot add BridgeBeats services if no IMusicLookupService(s) are available." );
            }

            // Register all BridgeBeats services (media link resolver, card services, playlist cleanup)
            bool useCaching = !string.IsNullOrWhiteSpace( settings.ATProtoIdentifier ) &&
                              !string.IsNullOrWhiteSpace( settings.ATProtoPassword );
            _ = services.AddBridgeBeatsServices(
                enabledProviders,
                useCaching,
                settings.BaseUrl,
                settings.CardCacheExpirationHours,
                settings.CardCacheCleanupInterval
            );
            _ = services.AddSingleton( enabledProviders );

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
        /// Validates Redis connectivity for caching. Redis is registered via Aspire.
        /// </summary>
        /// <param name="serviceProvider">The service provider to use for resolving services.</param>
        private static void InitializeCacheDatabase( IServiceProvider serviceProvider ) {
            Microsoft.Extensions.Logging.ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>( ).CreateLogger( "BridgeBeats.Web.Configuration.StartupExtensions" );
            try {
                // Validate Redis connection (fail-fast if unavailable)
                IConnectionMultiplexer? redis = serviceProvider.GetService<IConnectionMultiplexer>( );
                if (redis is null) {
                    logger.LogWarning( "BridgeBeats: Redis not configured - caching will be unavailable" );
                    return;
                }

                if (!redis.IsConnected) {
                    throw new InvalidOperationException( "Redis connection is not established. Check Redis configuration and connectivity." );
                }

                logger.LogInformation( "BridgeBeats: Redis cache connection established successfully" );
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to initialize Redis cache connection" );
                throw; // Fail-fast on Redis unavailability
            }
        }

        private static void ConfigureDatabases( IServiceCollection services, AppSettings settings ) {
            // Register DbContext factory for Identity only (SQLite)
            // Media link cache is now handled by Redis (see ConfigureATProtoIfEnabled)
            _ = services.AddDbContextFactory<ApplicationDbContext>( options =>
                options.UseSqlite(
                    settings.IdentityConnectionString,
                    b => b.MigrationsAssembly( "BridgeBeats.Core" )
                )
            );
        }

        private static void ConfigureIdentity( IServiceCollection services ) {
            // Use the centralized Identity configuration from Infrastructure project
            // This includes Data Protection configuration for [ProtectedPersonalData] attributes
            _ = services.AddBridgeBeatsIdentity( );
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
            // Register ATProto OAuth service (available even without server ATProto credentials)
            // This allows users to log in with Bluesky for playlist management
            if (!string.IsNullOrWhiteSpace( settings.BaseUrl )) {
                string clientId = $"{settings.BaseUrl.TrimEnd( '/' )}/.well-known/client-metadata.json";

                // Register HTTP client for OAuth token endpoint with standard resilience
                _ = services.AddHttpClient( "ATProtoOAuth" )
                    .AddStandardResilienceHandler( );

                _ = services.AddScoped<IATProtoOAuthService>( sp =>
                    new ATProtoOAuthService(
                        sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( ),
                        sp.GetRequiredService<ILogger<ATProtoOAuthService>>( ),
                        clientId,
                        sp.GetRequiredService<IHttpClientFactory>( )
                    )
                );

                // Register background service for cleaning up expired OAuth states
                _ = services.AddHostedService<OAuthStateCleanupService>( );
            }

            if (string.IsNullOrWhiteSpace( settings.ATProtoIdentifier ) ||
                string.IsNullOrWhiteSpace( settings.ATProtoPassword )) {
                return;
            }

            // Register ATProto session manager and storage service (centralized authentication)
            _ = services.AddATProtoSessionManager( settings.ATProtoIdentifier, settings.ATProtoPassword );
            _ = services.AddATProtoStorage( );

            // Register Redis-based cache service (requires IConnectionMultiplexer from Aspire)
            _ = services.AddSingleton<IMediaLinkCacheRepository>( s => new RedisMediaLinkCache(
                s.GetRequiredService<IConnectionMultiplexer>( ),
                s.GetRequiredService<IATProtoStorageService>( ),
                s.GetRequiredService<ILogger<RedisMediaLinkCache>>( ),
                settings.CacheDays,
                settings.ATProtoUserDID
            ) );

            // Register statistics service for the Statistics page
            Uri pdsUri = new( settings.ATProtoPdsUri ?? "https://pds.bridgebeats.link" );
            StatisticsSettings statsSettings = new(
                pdsUri,
                settings.ATProtoUserDID,
                TimeSpan.FromHours( 6 ) // Cache statistics for 6 hours
            );
            _ = services.AddSingleton( statsSettings );
            _ = services.AddSingleton<IStatisticsService>( s => new StatisticsService(
                s.GetRequiredService<IATProtoStorageService>( ),
                statsSettings,
                s.GetRequiredService<ILogger<StatisticsService>>( )
            ) );

            // Register queue infrastructure (deduplicator, rate limit tracker, saga manager)
            // and provider-specific queues for the LookupOrchestrator
            _ = services.AddQueueInfrastructure( );
            _ = services.AddAllProviderQueues<QueuedLookupRequest>( );

            // Register SQLite to Redis migration service (runs once at startup if SQLite DB exists)
            _ = services.AddSqliteToRedisMigration(
                settings.LinkCacheConnectionString,
                settings.CacheDays,
                settings.ATProtoUserDID
            );
        }

        private static HashSet<SupportedProviders> RegisterMusicProviders( IServiceCollection services, AppSettings settings ) {
            // When UseWorkerServices is true, register HTTP clients that call worker services
            // instead of direct provider implementations
            if (settings.Workers.UseWorkerServices) {
                // Still need AppleJwtHandler for MusicKit JS (AppleMusicController generates developer tokens)
                // and musickit-api HTTP client for user library access, even when using workers for lookups
                _ = services.AddAppleMusicJwtHandler(
                    settings.AppleTeamId,
                    settings.AppleKeyId,
                    settings.AppleKeyPath,
                    settings.Resilience.MaxRetryAfterSeconds
                );

                return services.AddMusicProviderHttpClients(
                    settings.Workers.SpotifyWorkerEnabled,
                    settings.Workers.AppleMusicWorkerEnabled,
                    settings.Workers.TidalWorkerEnabled
                );
            }

            // Otherwise, register direct provider implementations (legacy mode for testing/standalone)
            return services.AddMusicProviders(
                settings.AppleTeamId,
                settings.AppleKeyId,
                settings.AppleKeyPath,
                settings.SpotifyClientId,
                settings.SpotifyClientSecret,
                settings.TidalClientId,
                settings.TidalClientSecret,
                settings.Resilience.MaxRetryAfterSeconds
            );
        }

    }
}
