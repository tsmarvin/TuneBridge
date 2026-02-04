using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Tidal;
using Serilog;

namespace BridgeBeats.Worker.Tidal;

/// <summary>
/// Entry point for the Tidal worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the Tidal worker application.
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
    /// Configures the services for the Tidal worker application.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    private static void ConfigureServices( WebApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "Tidal" );

        // Add Aspire service defaults (health checks, telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire (for queue processing)
        builder.AddRedisClient( "redis" );

        // Read and validate credentials
        (string clientId, string clientSecret, int maxRetryAfterSeconds) = ValidateConfiguration( builder );

        // Register Tidal services
        HashSet<SupportedProviders> enabledProviders = [];
        _ = builder.Services.AddTidalServices( clientId, clientSecret, enabledProviders, maxRetryAfterSeconds );

        // Register JSON serializer options (required by TidalLookupService)
        _ = builder.Services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

        // Register queue processor background service for consuming from Redis streams
        _ = builder.Services.AddQueueProcessor<TidalLookupService>( SupportedProviders.Tidal );
    }

    /// <summary>
    /// Validates the required configuration for the Tidal worker.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>A tuple containing the validated credentials and configuration values.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
    private static (string ClientId, string ClientSecret, int MaxRetryAfterSeconds) ValidateConfiguration(
        WebApplicationBuilder builder
    ) {
        string? clientId = builder.Configuration["BridgeBeats:TidalClientId"];
        string? clientSecret = builder.Configuration["BridgeBeats:TidalClientSecret"];
        int maxRetryAfterSeconds = builder.Configuration.GetValue("BridgeBeats:Resilience:MaxRetryAfterSeconds", 120);

        return string.IsNullOrWhiteSpace( clientId ) || string.IsNullOrWhiteSpace( clientSecret )
            ? throw new InvalidOperationException(
                "Tidal credentials are required. Set BridgeBeats:TidalClientId and BridgeBeats:TidalClientSecret."
            )
            : ((string ClientId, string ClientSecret, int MaxRetryAfterSeconds))(clientId, clientSecret, maxRetryAfterSeconds);
    }

    /// <summary>
    /// Configures the endpoints for the Tidal worker application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static void ConfigureEndpoints( WebApplication app ) {
        // Map Aspire health check endpoints
        _ = app.MapDefaultEndpoints( );

        // Map provider lookup endpoints using shared extension
        _ = app.MapProviderLookupEndpoints<TidalLookupService>( );
    }
}
