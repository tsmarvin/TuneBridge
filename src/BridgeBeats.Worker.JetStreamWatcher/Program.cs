using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.ServiceDefaults;
using Serilog;

namespace BridgeBeats.Worker.JetStreamWatcher;

/// <summary>
/// Entry point for the JetStreamWatcher worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the JetStreamWatcher worker application.
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
    /// Configures the services for the JetStreamWatcher worker application.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    private static void ConfigureServices( WebApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "JetStreamWatcher" );

        // Add Aspire service defaults (health checks, telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire (for queue submission)
        builder.AddRedisClient( "redis" );

        // Register queue infrastructure for submitting to provider queues
        _ = builder.Services.AddQueueInfrastructure( );
        _ = builder.Services.AddAllProviderQueues<QueuedLookupRequest>( );

        // Register JetStream watcher background service
        _ = builder.Services.AddHostedService<JetStreamWatcherService>( );
    }

    /// <summary>
    /// Configures the endpoints for the JetStreamWatcher worker application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static void ConfigureEndpoints( WebApplication app ) {
        // Map Aspire health check endpoints
        _ = app.MapDefaultEndpoints( );
    }
}
