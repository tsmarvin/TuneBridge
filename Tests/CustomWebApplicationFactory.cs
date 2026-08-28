using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Providers.AppleMusic;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Core.Domain.Providers.Tidal;
using BridgeBeats.Core.Domain.Services.LinkResolver;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Core.Infrastructure.Settings;
using BridgeBeats.Web.Authentication;
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
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> over the BridgeBeats web app's entry point that
/// hosts the real application in-memory for integration and end-to-end tests. It points the app at the
/// shared Redis container, replaces the identity store with a per-instance SQLite database, applies a
/// fast retry/timeout resilience profile, and seeds the isolated settings store through the same
/// production bootstrap path used by AppHost.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Web.Program> {
    /// <summary>The in-memory configuration overlaid on the host's normal configuration sources.</summary>
    private readonly Dictionary<string, string?> _configData;
    /// <summary>Filesystem root containing this factory's disposable database and key ring.</summary>
    private readonly string _artifactRoot;
    /// <summary>Filesystem path of this instance's isolated SQLite identity database.</summary>
    private readonly string _identityDbPath;

    /// <summary>
    /// Creates a factory with the default test configuration and no overrides.
    /// </summary>
    public CustomWebApplicationFactory( ) : this( null ) { }

    /// <summary>
    /// Creates a factory, optionally overlaying caller-supplied configuration on top of the test
    /// defaults. Requires the shared Redis container and provisions a unique SQLite identity database;
    /// the Redis connection keys are protected and cannot be overridden.
    /// </summary>
    /// <param name="configOverrides">
    /// Optional configuration key/value overrides; protected Redis keys are ignored.
    /// </param>
    public CustomWebApplicationFactory( Dictionary<string, string?>? configOverrides ) {
        // IMPORTANT: Require Redis FIRST, before accessing RedisConnectionString.
        // This ensures the container is started and the connection string is populated.
        SharedTestInfrastructure.RequireRedis( );

        // Generate unique database file paths for this test instance
        _artifactRoot = TestArtifacts.CreateDirectory( "web-factory" );
        _identityDbPath = Path.Combine( _artifactRoot, "bridgebeats.db" );
        string dataProtectionKeyPath = Path.Combine( _artifactRoot, "keys" );

        // Get the Redis connection string AFTER RequireRedis() has initialized the container
        string redisConnectionString = SharedTestInfrastructure.RedisConnectionString;

        // Default test configuration (Spotify only)
        Dictionary<string, string?> defaults = new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = "", // Empty string to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = $"Data Source={_identityDbPath}",
            ["BridgeBeats:DataProtectionKeyPath"] = dataProtectionKeyPath,
            ["BridgeBeats:ApiKeySalt"] = "test-api-key-salt-32-characters!",
            ["BridgeBeats:Bootstrap:SeedSettings"] = "true",
            ["BridgeBeats:Domain"] = "localhost",
            // Redis connection from shared test infrastructure (captured AFTER initialization)
            // Set both keys to ensure Aspire can find the connection string
            ["ConnectionStrings:redis"] = redisConnectionString,
            ["Aspire:StackExchange:Redis:ConnectionString"] = redisConnectionString,
        };

        defaults["BridgeBeats:ATProtoIdentifier"] = string.Empty;
        defaults["BridgeBeats:ATProtoPassword"] = string.Empty;
        defaults["BridgeBeats:ATProtoUserDID"] = string.Empty;

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

        }

        // A developer may still have the retired AppleKeyPath user-secret alongside the two
        // identifiers. Startup seeding accepts only key contents, so treat that incomplete legacy
        // group as unconfigured instead of persisting an invalid aggregate.
        if (!_configData.TryGetValue( "BridgeBeats:ApplePrivateKey", out string? applePrivateKey ) ||
            string.IsNullOrWhiteSpace( applePrivateKey )) {
            _configData["BridgeBeats:AppleTeamId"] = string.Empty;
            _configData["BridgeBeats:AppleKeyId"] = string.Empty;
            _configData["BridgeBeats:ApplePrivateKey"] = string.Empty;
        }

        string identityConnectionString = _configData["BridgeBeats:IdentityConnectionString"]!;
        dataProtectionKeyPath = _configData["BridgeBeats:DataProtectionKeyPath"]!;
        IConfiguration seedConfiguration = new ConfigurationBuilder( )
            .AddInMemoryCollection( _configData )
            .Build( );
        ApplicationSettingsSnapshot snapshot = ApplicationSettingsBootstrapper.MigrateSeedAndLoadAsync(
            identityConnectionString,
            dataProtectionKeyPath,
            seedConfiguration
        ).GetAwaiter( ).GetResult( ) ?? throw new InvalidOperationException(
            "The test settings database was not seeded."
        );

        foreach (KeyValuePair<string, string?> pair in ApplicationSettingsRuntimeProjector.ProjectWeb(
            snapshot,
            identityConnectionString,
            dataProtectionKeyPath,
            _configData["BridgeBeats:Domain"] ?? "localhost"
        )) {
            // Empty strings deliberately suppress lower-priority developer user-secrets for
            // unconfigured values without mutating process-wide environment variables.
            _configData[pair.Key] = pair.Value ?? string.Empty;
        }
    }

    /// <summary>
    /// Configures the test host: switches to the <c>Testing</c> environment, replaces any in-memory
    /// configuration with the test configuration, repoints the Redis connection and identity
    /// <c>DbContext</c> at the test infrastructure, and registers a fast resilience profile.
    /// </summary>
    /// <param name="builder">The web host builder supplied by the test host.</param>
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

            // Remove existing DbContext factory registrations
            _ = services.RemoveAll<IDbContextFactory<ApplicationDbContext>>( );
            _ = services.RemoveAll<ApplicationDbContext>( );

            // Re-register the same protected factory used by production against the isolated test database.
            _ = services.AddBridgeBeatsApplicationDatabase( identityConnStr );

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

            _ = services.PostConfigure<InternalServiceAuthOptions>(
                InternalServiceDefaults.AuthenticationScheme,
                options => {
                    options.ServiceKey = _configData.TryGetValue(
                        "BridgeBeats:InternalServiceKey",
                        out string? serviceKey
                    ) ? serviceKey ?? string.Empty : string.Empty;
                }
            );

            bool useDirectMode = _configData.TryGetValue(
                "BridgeBeats:Workers:UseWorkerServices",
                out string? workerMode
            ) && workerMode?.Equals( "false", StringComparison.OrdinalIgnoreCase ) == true;

            if (useDirectMode) {
                _ = services.RemoveAll<IMediaLinkService>( );
                _ = services.RemoveAll<ILookupOrchestrator>( );
                _ = services.AddTransient<IMediaLinkService>( sp => {
                    HashSet<SupportedProviders> enabledProviders =
                        sp.GetRequiredService<HashSet<SupportedProviders>>( );
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
    /// Applies pending EF Core migrations to the isolated identity database and seeds the Aspire
    /// dashboard access role. Call once after the host is built and before exercising endpoints that
    /// touch identity.
    /// </summary>
    public async Task InitializeDatabasesAsync( ) {
        // Get service provider from the server
        IServiceProvider services = Services;

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
    /// Disposes the test host and deletes this instance's SQLite database and key ring.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <c>Dispose</c> rather than a finalizer.</param>
    protected override void Dispose( bool disposing ) {
        base.Dispose( disposing );

        if (disposing) {
            TryDeleteDirectory( _artifactRoot );
        }
    }

    /// <summary>
    /// Deletes the file at the given path if it exists, swallowing any I/O errors during cleanup.
    /// </summary>
    /// <param name="path">The file path to delete.</param>
    private static void TryDeleteDirectory( string path ) {
        try {
            if (Directory.Exists( path )) {
                Directory.Delete( path, recursive: true );
            }
        } catch {
            // A locked artifact remains under TestResults for diagnosis and later cleanup.
        }
    }
}
