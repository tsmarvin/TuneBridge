using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
            ["BridgeBeats:IdentityConnectionString"] = "Data Source=Identity;Mode=Memory",
            ["BridgeBeats:ApiKeySalt"] = "api_key_salt",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:LinkCacheConnectionString"] = "Data Source=LinkCache;Mode=Memory;Cache=Shared",
        };
        _ = builder.UseEnvironment( "Testing" );

        _ = builder.ConfigureAppConfiguration( ( context, config ) => {
            // Clear all existing configuration sources to prevent appsettings.json from loading
            config.Sources.Clear( );
            // Add our test configuration as the only source
            _ = config.AddInMemoryCollection( configData );
        } );

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
}
