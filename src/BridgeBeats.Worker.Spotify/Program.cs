using System.Text.Json;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Providers.Spotify;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using BridgeBeats.Worker.Common;
using Serilog;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Entry point for the Spotify worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the Spotify worker application.
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
    /// Configures the services for the Spotify worker application.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    private static void ConfigureServices( WebApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "Spotify" );

        // Add Aspire service defaults (health checks, telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire (for queue processing)
        builder.AddRedisClient( "redis" );

        // Read and validate credentials
        (string clientId, string clientSecret, int maxRetryAfterSeconds) = ValidateConfiguration( builder );

        // Register Spotify services
        HashSet<SupportedProviders> enabledProviders = [];
        _ = builder.Services.AddSpotifyServices( clientId, clientSecret, enabledProviders, maxRetryAfterSeconds );

        // Register JSON serializer options (required by SpotifyLookupService)
        _ = builder.Services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

        // Register genre cache for artist genre processing
        _ = builder.Services.AddGenreCache( );

        // Register queue processor background service for consuming from Redis streams
        _ = builder.Services.AddQueueProcessor<SpotifyLookupService>( SupportedProviders.Spotify );

        // Register Spotify-specific batch queue helper and bulk processor service
        _ = builder.Services.AddSingleton<SpotifyBatchQueueHelper>( );
        _ = builder.Services.AddHostedService<SpotifyBulkProcessorService>( );

        // Register Spotify artist genre service for scheduled genre fetching
        _ = builder.Services.AddHostedService<SpotifyArtistGenreService>( );
    }

    /// <summary>
    /// Validates the required configuration for the Spotify worker.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>A tuple containing the validated credentials and configuration values.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
    private static (string ClientId, string ClientSecret, int MaxRetryAfterSeconds) ValidateConfiguration(
        WebApplicationBuilder builder
    ) {
        string? clientId = builder.Configuration["BridgeBeats:SpotifyClientId"];
        string? clientSecret = builder.Configuration["BridgeBeats:SpotifyClientSecret"];
        int maxRetryAfterSeconds = builder.Configuration.GetValue("BridgeBeats:Resilience:MaxRetryAfterSeconds", 120);

        return string.IsNullOrWhiteSpace( clientId ) || string.IsNullOrWhiteSpace( clientSecret )
            ? throw new InvalidOperationException(
                "Spotify credentials are required. Set BridgeBeats:SpotifyClientId and BridgeBeats:SpotifyClientSecret."
            )
            : ((string ClientId, string ClientSecret, int MaxRetryAfterSeconds))(clientId, clientSecret, maxRetryAfterSeconds);
    }

    /// <summary>
    /// Configures the endpoints for the Spotify worker application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static void ConfigureEndpoints( WebApplication app ) {
        // Map Aspire health check endpoints
        _ = app.MapDefaultEndpoints( );

        // Map provider lookup endpoints using shared extension
        _ = app.MapProviderLookupEndpoints<SpotifyLookupService>( );
    }
}
