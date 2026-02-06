using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Infrastructure.Extensions;
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
        HostApplicationBuilder builder = Host.CreateApplicationBuilder( args );

        ConfigureServices( builder );

        IHost app = builder.Build();

        try {
            app.Run( );
        } finally {
            Log.CloseAndFlush( );
        }
    }

    /// <summary>
    /// Configures the services for the JetStreamWatcher worker application.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    private static void ConfigureServices( HostApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "JetStreamWatcher" );

        // Add Aspire service defaults (telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire (for queue submission)
        builder.AddRedisClient( "redis" );

        // Register queue infrastructure for submitting to provider queues
        _ = builder.Services.AddQueueInfrastructure( );
        _ = builder.Services.AddAllProviderQueues<QueuedLookupRequest>( );

        // Register JetStream watcher background service
        _ = builder.Services.AddHostedService<JetStreamWatcherService>( );
    }
}
