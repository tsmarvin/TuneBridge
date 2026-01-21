using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Worker.JetStreamWatcher;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Add Redis client from Aspire (for queue submission)
builder.AddRedisClient( "redis" );

// Register queue infrastructure for submitting to provider queues
_ = builder.Services.AddQueueInfrastructure( );
_ = builder.Services.AddAllProviderQueues<QueuedLookupRequest>( );

// Register JetStream watcher background service
_ = builder.Services.AddHostedService<JetStreamWatcherService>( );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

app.Run( );
