using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Identity;
using BridgeBeats.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace BridgeBeats.Tests;

/// <summary>
/// Custom web application factory for integration testing.
/// Ensures test configuration completely overrides any file-based configuration (like appsettings.json).
/// Uses file-based SQLite databases with unique names per factory instance to ensure test isolation.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program> {
    private readonly Dictionary<string, string?> _configData;
    private readonly string _identityDbPath;
    private readonly string _linkCacheDbPath;

    public CustomWebApplicationFactory( ) : this( null ) { }

    public CustomWebApplicationFactory( Dictionary<string, string?>? configOverrides ) {
        // Generate unique database file paths for this test instance
        string uniqueId = Guid.NewGuid().ToString( "N" )[..8];
        _identityDbPath = Path.Combine( Path.GetTempPath( ), $"BridgeBeats_Identity_{uniqueId}.db" );
        _linkCacheDbPath = Path.Combine( Path.GetTempPath( ), $"BridgeBeats_LinkCache_{uniqueId}.db" );

        // Default test configuration (Spotify only)
        Dictionary<string, string?> defaults = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = "", // Empty string to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source={_identityDbPath}",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:LinkCacheConnectionString"] = $"Data Source={_linkCacheDbPath}",
        };

        // Merge overrides onto defaults (overrides win)
        _configData = defaults;
        if (configOverrides is not null) {
            foreach (KeyValuePair<string, string?> kvp in configOverrides) {
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

    protected override void ConfigureWebHost( IWebHostBuilder builder ) {
        _ = builder.UseEnvironment( "Testing" );

        // Ensure Spotify credentials are set for tests (required for service registration)
        if (!_configData.TryGetValue( "BridgeBeats:SpotifyClientId", out string? spotifyClientId ) || string.IsNullOrWhiteSpace( spotifyClientId )) {
            _configData["BridgeBeats:SpotifyClientId"] = "test";
        }
        if (!_configData.TryGetValue( "BridgeBeats:SpotifyClientSecret", out string? spotifyClientSecret ) || string.IsNullOrWhiteSpace( spotifyClientSecret )) {
            _configData["BridgeBeats:SpotifyClientSecret"] = "test";
        }

        _ = builder.ConfigureAppConfiguration( ( context, config ) => {
            // Remove any existing in-memory collections to avoid conflicts
            IConfigurationSource[] existingSources = config.Sources.ToArray();
            config.Sources.Clear();
            
            // Add back non-memory sources (like environment variables, command line, etc.)
            foreach (IConfigurationSource source in existingSources) {
                if (source is not Microsoft.Extensions.Configuration.Memory.MemoryConfigurationSource) {
                    config.Sources.Add( source );
                }
            }
            
            // Add test configuration as the highest priority source (last wins)
            _ = config.AddInMemoryCollection( _configData );
        } );

        // Override services registered by the application
        _ = builder.ConfigureServices( services => {
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
