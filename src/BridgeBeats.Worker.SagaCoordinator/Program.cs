using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.Infrastructure.Storage;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using BridgeBeats.Worker.SagaCoordinator;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Add Redis client from Aspire
builder.AddRedisClient( "redis" );

// Read ATProto credentials from configuration
string? atProtoIdentifier = builder.Configuration["BridgeBeats:ATProtoIdentifier"];
string? atProtoPassword = builder.Configuration["BridgeBeats:ATProtoPassword"];
string? atProtoUserDID = builder.Configuration["BridgeBeats:ATProtoUserDID"];
int cacheDays = builder.Configuration.GetValue( "BridgeBeats:CacheDays", 30 );

// Validate ATProto credentials
if (string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
    string.IsNullOrWhiteSpace( atProtoPassword ) ||
    string.IsNullOrWhiteSpace( atProtoUserDID )) {
    throw new InvalidOperationException(
        "ATProto credentials are required. Set BridgeBeats:ATProtoIdentifier, BridgeBeats:ATProtoPassword, and BridgeBeats:ATProtoUserDID."
    );
}

// Queue settings use defaults from the record definition

// Register queue infrastructure services
_ = builder.Services.AddQueueInfrastructure( );

// Register ATProto storage service
_ = builder.Services.AddSingleton<IATProtoStorageService>( sp =>
    new ATProtoStorageService(
        atProtoIdentifier,
        atProtoPassword,
        sp.GetRequiredService<ILogger<ATProtoStorageService>>( )
    )
);

// Register the cache repository
_ = builder.Services.AddSingleton<IMediaLinkCacheRepository>( sp =>
    new RedisMediaLinkCache(
        sp.GetRequiredService<IConnectionMultiplexer>( ),
        sp.GetRequiredService<IATProtoStorageService>( ),
        sp.GetRequiredService<ILogger<RedisMediaLinkCache>>( ),
        cacheDays,
        atProtoUserDID
    )
);

// Register the result combiner
_ = builder.Services.AddSingleton<SagaResultCombiner>( );

// Register the coordinator background service
_ = builder.Services.AddHostedService( sp => new SagaCoordinatorBackgroundService(
    sp.GetRequiredService<IConnectionMultiplexer>( ),
    sp.GetRequiredService<ISagaStateManager>( ),
    sp.GetRequiredService<IATProtoStorageService>( ),
    sp.GetRequiredService<IMediaLinkCacheRepository>( ),
    sp.GetRequiredService<IRequestDeduplicator>( ),
    sp.GetRequiredService<SagaResultCombiner>( ),
    sp.GetRequiredService<ILogger<SagaCoordinatorBackgroundService>>( )
) );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

// Simple status endpoint
app.MapGet( "/status", ( ) => new {
    Service = "SagaCoordinator",
    Status = "Running",
    Timestamp = DateTimeOffset.UtcNow
} );

app.Run( );
