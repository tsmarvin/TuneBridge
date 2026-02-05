using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.AppleMusic;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Domain.Providers.Tidal;
using BridgeBeats.Core.Domain.Services.LinkResolver;
using BridgeBeats.Core.Infrastructure.Cache;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Tests;

/// <summary>
/// Custom web application factory for integration testing.
/// Ensures test configuration completely overrides any file-based configuration (like appsettings.json).
/// Uses file-based SQLite databases with unique names per factory instance to ensure test isolation.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Web.Program> {
    private readonly Dictionary<string, string?> _configData;
    private readonly string _identityDbPath;
    private readonly string _linkCacheDbPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomWebApplicationFactory"/> with default configuration.
    /// </summary>
    public CustomWebApplicationFactory( ) : this( null ) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomWebApplicationFactory"/> with optional configuration overrides.
    /// </summary>
    /// <param name="configOverrides">Optional dictionary of configuration values to override defaults.</param>
    public CustomWebApplicationFactory( Dictionary<string, string?>? configOverrides ) {
        // IMPORTANT: Require Redis FIRST, before accessing RedisConnectionString.
        // This ensures the container is started and the connection string is populated.
        SharedTestInfrastructure.RequireRedis( );

        // Generate unique database file paths for this test instance
        string uniqueId = Guid.NewGuid().ToString( "N" )[..8];
        _identityDbPath = Path.Combine( Path.GetTempPath( ), $"BridgeBeats_Identity_{uniqueId}.db" );
        _linkCacheDbPath = Path.Combine( Path.GetTempPath( ), $"BridgeBeats_LinkCache_{uniqueId}.db" );

        // Get the Redis connection string AFTER RequireRedis() has initialized the container
        string redisConnectionString = SharedTestInfrastructure.RedisConnectionString;

        // Default test configuration (Spotify only)
        Dictionary<string, string?> defaults = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = "", // Empty string to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source={_identityDbPath}",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:ATProtoUserDID"] = "", // Disable ATProto by default in tests
            ["BridgeBeats:LinkCacheConnectionString"] = $"Data Source={_linkCacheDbPath}",
            // Redis connection from shared test infrastructure (captured AFTER initialization)
            // Set both keys to ensure Aspire can find the connection string
            ["ConnectionStrings:redis"] = redisConnectionString,
            ["Aspire:StackExchange:Redis:ConnectionString"] = redisConnectionString,
        };

        // CRITICAL: Set environment variables for ATProto settings to ensure they override user secrets.
        // The WebApplicationFactory's ConfigureAppConfiguration runs AFTER the app binds configuration,
        // but environment variables are loaded via AddEnvironmentVariables() which runs during app startup.
        // This ensures test overrides take effect before DID validation runs.
        Environment.SetEnvironmentVariable( "BridgeBeats__ATProtoIdentifier", "" );
        Environment.SetEnvironmentVariable( "BridgeBeats__ATProtoPassword", "" );
        Environment.SetEnvironmentVariable( "BridgeBeats__ATProtoUserDID", "" );

        // Merge overrides onto defaults (overrides win), EXCEPT for critical test infrastructure keys
        // that must always use the test container connection
        HashSet<string> protectedKeys = [
            "ConnectionStrings:redis",
            "Aspire:StackExchange:Redis:ConnectionString",
        ];

        _configData = defaults;
        if (configOverrides is not null) {
            foreach (KeyValuePair<string, string?> kvp in configOverrides) {
                // Skip protected keys - test infrastructure values must not be overridden
                if (protectedKeys.Contains( kvp.Key )) {
                    continue;
                }
                _configData[kvp.Key] = kvp.Value;
            }

            if (configOverrides.TryGetValue( "BridgeBeats:IdentityConnectionString", out string? identityCs ) && identityCs != null) {
                _identityDbPath = ExtractDataSource( identityCs );
            }
            if (configOverrides.TryGetValue( "BridgeBeats:LinkCacheConnectionString", out string? linkCacheCs ) && linkCacheCs != null) {
                _linkCacheDbPath = ExtractDataSource( linkCacheCs );
            }
        }
    }

    private static string ExtractDataSource( string connectionString ) {
        // Extract Data Source path from connection string
        string[] parts = connectionString.Split( ';' );
        foreach (string part in parts) {
            if (part.Trim( ).StartsWith( "Data Source=", StringComparison.OrdinalIgnoreCase )) {
                return part.Trim( )["Data Source=".Length..];
            }
        }
        return connectionString; // Return original if parsing fails
    }

    /// <summary>
    /// Configures the web host for testing with test-specific configuration and services.
    /// </summary>
    /// <param name="builder">The web host builder to configure.</param>
    protected override void ConfigureWebHost( IWebHostBuilder builder ) {
        _ = builder.UseEnvironment( "Testing" );

        _ = builder.ConfigureAppConfiguration( ( context, config ) => {
            // Remove any existing in-memory collections to avoid conflicts
            IConfigurationSource[] existingSources = [.. config.Sources];
            config.Sources.Clear( );

            // Add back non-memory sources (like environment variables, command line, etc.)
            foreach (IConfigurationSource source in existingSources.Where( s => s is not Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource )) {
                config.Sources.Add( source );
            }

            // Add test configuration as the highest priority source (last wins)
            _ = config.AddInMemoryCollection( _configData );
        } );

        // Override services registered by the application
        _ = builder.ConfigureServices( ( context, services ) => {
            // CRITICAL: Replace the Aspire Redis client registration with a direct connection
            // The Aspire AddRedisClient doesn't work properly with WebApplicationFactory because
            // it captures configuration before ConfigureAppConfiguration runs.
            _ = services.RemoveAll<IConnectionMultiplexer>( );
            _ = services.AddSingleton<IConnectionMultiplexer>( _ => {
                string connStr = _configData.TryGetValue( "ConnectionStrings:redis", out string? cs ) && !string.IsNullOrEmpty( cs )
                    ? cs
                    : SharedTestInfrastructure.RedisConnectionString;
                return ConnectionMultiplexer.Connect( connStr );
            } );

            // Get the connection strings from our config (they may have been overridden)
            string identityConnStr = _configData.TryGetValue( "BridgeBeats:IdentityConnectionString", out string? idCs ) && idCs != null
                ? idCs
                : $"Data Source={_identityDbPath}";
            string linkCacheConnStr = _configData.TryGetValue( "BridgeBeats:LinkCacheConnectionString", out string? lcCs ) && lcCs != null
                ? lcCs
                : $"Data Source={_linkCacheDbPath}";

            // Remove existing DbContext factory registrations
            _ = services.RemoveAll<IDbContextFactory<ApplicationDbContext>>( );
            _ = services.RemoveAll<IDbContextFactory<MediaLinkCacheDbContext>>( );
            _ = services.RemoveAll<ApplicationDbContext>( );

            // Re-register DbContext factories with test connection strings
            _ = services.AddDbContextFactory<ApplicationDbContext>( options =>
                options.UseSqlite( identityConnStr )
            );

            _ = services.AddDbContextFactory<MediaLinkCacheDbContext>( options =>
                options.UseSqlite( linkCacheConnStr )
            );

            // Re-register scoped ApplicationDbContext for Identity
            _ = services.AddScoped( sp => {
                IDbContextFactory<ApplicationDbContext> factory = sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
                return factory.CreateDbContext( );
            } );

            // Add configuration for test-specific resilience
            _ = services.Configure<HttpStandardResilienceOptions>( "test-resilience", options => {
                options.Retry.MaxRetryAttempts = 2; // Reduce retries in tests
                options.Retry.Delay = TimeSpan.FromMilliseconds( 500 );
                options.Retry.MaxDelay = TimeSpan.FromSeconds( 5 ); // Much shorter max delay
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.UseJitter = false; // Disable jitter for predictable test timing

                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds( 20 ); // Shorter total timeout
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds( 5 ); // Shorter per-attempt timeout
            } );

            // CRITICAL: Check if we're in direct provider mode (bypassing queue/caching)
            // This fixes the timing issue where the application's service registration happens before
            // our configuration overrides are applied. The application may have registered
            // CachingMediaLinkService (which uses LookupOrchestrator and requires workers) based on
            // the original configuration. We need to explicitly replace it with DefaultMediaLinkService
            // for tests that want direct provider access.
            bool useDirectMode = _configData.TryGetValue( "BridgeBeats:Workers:UseWorkerServices", out string? workerMode )
                && workerMode?.Equals( "false", StringComparison.OrdinalIgnoreCase ) == true;

            if (useDirectMode) {
                // Remove the potentially-registered caching service and orchestrator
                _ = services.RemoveAll<IMediaLinkService>( );
                _ = services.RemoveAll<ILookupOrchestrator>( );

                // Re-register DefaultMediaLinkService for direct provider access
                _ = services.AddTransient<IMediaLinkService>( sp => {
                    // Get enabled providers from the service provider (registered by the application)
                    HashSet<SupportedProviders> enabledProviders = sp.GetRequiredService<HashSet<SupportedProviders>>( );

                    // Build provider dictionary from registered lookup services
                    Dictionary<SupportedProviders, IMusicLookupService> providerServices = [];
                    foreach (SupportedProviders provider in enabledProviders) {
                        IMusicLookupService? lookupService = provider switch {
                            SupportedProviders.Spotify => sp.GetService<SpotifyLookupService>( ),
                            SupportedProviders.AppleMusic => sp.GetService<AppleMusicLookupService>( ),
                            SupportedProviders.Tidal => sp.GetService<TidalLookupService>( ),
                            _ => null
                        };
                        if (lookupService is not null) {
                            providerServices.Add( provider, lookupService );
                        }
                    }

                    return new DefaultMediaLinkService(
                        providerServices,
                        sp.GetRequiredService<ILogger<DefaultMediaLinkService>>( ),
                        sp.GetRequiredService<JsonSerializerOptions>( )
                    );
                } );
            }
        } );
    }

    /// <summary>
    /// Initializes the test databases after the server has been created and services configured.
    /// Must be called after CreateClient() or accessing Services to ensure the server is running.
    /// </summary>
    public async Task InitializeDatabasesAsync( ) {
        // Get service provider from the server
        IServiceProvider services = Services;

        // Initialize cache database
        using (IServiceScope scope = services.CreateScope( )) {
            IDbContextFactory<MediaLinkCacheDbContext>? cacheFactory = scope.ServiceProvider.GetService<IDbContextFactory<MediaLinkCacheDbContext>>( );
            if (cacheFactory is not null) {
                using MediaLinkCacheDbContext cacheContext = cacheFactory.CreateDbContext( );
                await cacheContext.Database.MigrateAsync( );
            }
        }

        // Initialize identity database and seed roles
        using (IServiceScope scope = services.CreateScope( )) {
            IDbContextFactory<ApplicationDbContext> identityFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>( );
            using ApplicationDbContext identityContext = identityFactory.CreateDbContext( );
            await identityContext.Database.MigrateAsync( );
        }

        // Seed roles using the same scope approach as the production code
        using (IServiceScope scope = services.CreateScope( )) {
            Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole> roleManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>( );

            const string AspireDashboardRole = "AspireDashboardAccess";
            if (!await roleManager.RoleExistsAsync( AspireDashboardRole )) {
                _ = await roleManager.CreateAsync( new Microsoft.AspNetCore.Identity.IdentityRole( AspireDashboardRole ) );
            }
        }
    }

    /// <summary>
    /// Disposes of resources including temporary database files.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected override void Dispose( bool disposing ) {
        base.Dispose( disposing );

        if (disposing) {
            // Clean up temp database files
            TryDeleteFile( _identityDbPath );
            TryDeleteFile( _linkCacheDbPath );
        }
    }

    private static void TryDeleteFile( string path ) {
        try {
            if (File.Exists( path )) {
                File.Delete( path );
            }
        } catch {
            // Ignore cleanup failures - temp files will be cleaned up eventually
        }
    }
}
