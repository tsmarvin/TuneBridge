using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace BridgeBeats.Tests;

/// <summary>
/// Custom web application factory for integration testing.
/// Ensures test configuration completely overrides any file-based configuration (like appsettings.json).
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program> {
    private readonly Dictionary<string, string?>? _configOverrides;
    private SqliteConnection? _identityConnection;
    private SqliteConnection? _linkCacheConnection;

    public CustomWebApplicationFactory( ) { }

    public CustomWebApplicationFactory( Dictionary<string, string?> configOverrides ) {
        _configOverrides = configOverrides;
    }

    protected override void ConfigureWebHost( IWebHostBuilder builder ) {
        // Default test configuration (Spotify only) unless overrides are provided
        Dictionary<string, string?> configData = _configOverrides ?? new( ) {
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test",
            ["BridgeBeats:DiscordToken"] = null, // Explicitly null to prevent Discord service registration
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory;Cache=Shared",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };

        // Keep connections open for in-memory databases to prevent them from being destroyed
        if (configData["BridgeBeats:IdentityConnectionString"]?.Contains( "Mode=Memory", StringComparison.OrdinalIgnoreCase ) == true) {
            _identityConnection = new SqliteConnection( configData["BridgeBeats:IdentityConnectionString"] );
            _identityConnection.Open( );
        }
        if (configData["BridgeBeats:LinkCacheConnectionString"]?.Contains( "Mode=Memory", StringComparison.OrdinalIgnoreCase ) == true) {
            _linkCacheConnection = new SqliteConnection( configData["BridgeBeats:LinkCacheConnectionString"] );
            _linkCacheConnection.Open( );
        }

        _ = builder.UseEnvironment( "Testing" );

        _ = builder.UseSetting( "BridgeBeats:SpotifyClientId", configData["BridgeBeats:SpotifyClientId"] );
        _ = builder.UseSetting( "BridgeBeats:SpotifyClientSecret", configData["BridgeBeats:SpotifyClientSecret"] );
        _ = builder.UseSetting( "BridgeBeats:DiscordToken", configData["BridgeBeats:DiscordToken"] );
        _ = builder.UseSetting( "BridgeBeats:IdentityConnectionString", configData["BridgeBeats:IdentityConnectionString"] );
        _ = builder.UseSetting( "BridgeBeats:ApiKeySalt", configData["BridgeBeats:ApiKeySalt"] );
        _ = builder.UseSetting( "BridgeBeats:ATProtoIdentifier", configData["BridgeBeats:ATProtoIdentifier"] );
        _ = builder.UseSetting( "BridgeBeats:ATProtoPassword", configData["BridgeBeats:ATProtoPassword"] );
        _ = builder.UseSetting( "BridgeBeats:LinkCacheConnectionString", configData["BridgeBeats:LinkCacheConnectionString"] );

        // Override HTTP client resilience settings for faster test execution
        _ = builder.ConfigureServices( services => {
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

    protected override void Dispose( bool disposing ) {
        if (disposing) {
            _identityConnection?.Close( );
            _identityConnection?.Dispose( );
            _linkCacheConnection?.Close( );
            _linkCacheConnection?.Dispose( );
        }
        base.Dispose( disposing );
    }
}
