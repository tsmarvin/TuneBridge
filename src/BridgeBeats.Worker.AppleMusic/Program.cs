using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.AppleMusic;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using BridgeBeats.Worker.Common;
using Serilog;

namespace BridgeBeats.Worker.AppleMusic;

/// <summary>
/// Entry point for the Apple Music worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the Apple Music worker application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static void Main( string[] args ) {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        ConfigureServices( builder );

        WebApplication app = builder.Build();

        ConfigureEndpoints( app );

        try {
            app.Run( );
        } finally {
            Log.CloseAndFlush( );
        }
    }

    /// <summary>
    /// Configures the services for the Apple Music worker application.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    private static void ConfigureServices( WebApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "AppleMusic" );

        // Add Aspire service defaults (health checks, telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire (for queue processing)
        builder.AddRedisClient( "redis" );

        // Read and validate credentials
        (string teamId, string keyId, string keyPath, int maxRetryAfterSeconds) = ValidateConfiguration( builder );

        // Register Apple Music services
        HashSet<SupportedProviders> enabledProviders = [];
        _ = builder.Services.AddAppleMusicServices( teamId, keyId, keyPath, enabledProviders, maxRetryAfterSeconds );

        // Register JSON serializer options (required by AppleMusicLookupService)
        _ = builder.Services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

        // Register queue processor background service for consuming from Redis streams
        _ = builder.Services.AddQueueProcessor<AppleMusicLookupService>( SupportedProviders.AppleMusic );
    }

    /// <summary>
    /// Validates the required configuration for the Apple Music worker.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>A tuple containing the validated credentials and configuration values.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
    private static (string TeamId, string KeyId, string KeyPath, int MaxRetryAfterSeconds) ValidateConfiguration(
        WebApplicationBuilder builder
    ) {
        string? teamId = builder.Configuration["BridgeBeats:AppleTeamId"];
        string? keyId = builder.Configuration["BridgeBeats:AppleKeyId"];
        string? keyPath = builder.Configuration["BridgeBeats:AppleKeyPath"];
        int maxRetryAfterSeconds = builder.Configuration.GetValue("BridgeBeats:Resilience:MaxRetryAfterSeconds", 120);

        return string.IsNullOrWhiteSpace( teamId ) ||
            string.IsNullOrWhiteSpace( keyId ) ||
            string.IsNullOrWhiteSpace( keyPath )
            ? throw new InvalidOperationException(
                "Apple Music credentials are required. Set BridgeBeats:AppleTeamId, BridgeBeats:AppleKeyId, and BridgeBeats:AppleKeyPath."
            )
            : ((string TeamId, string KeyId, string KeyPath, int MaxRetryAfterSeconds))(teamId, keyId, keyPath, maxRetryAfterSeconds);
    }

    /// <summary>
    /// Configures the endpoints for the Apple Music worker application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static void ConfigureEndpoints( WebApplication app ) {
        // Map Aspire health check endpoints
        _ = app.MapDefaultEndpoints( );

        // Map provider lookup endpoints using shared extension
        _ = app.MapProviderLookupEndpoints<AppleMusicLookupService>( );
    }
}
