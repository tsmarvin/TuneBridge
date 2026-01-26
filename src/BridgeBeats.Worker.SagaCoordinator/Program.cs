using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Queue;
using BridgeBeats.Infrastructure.Storage;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using BridgeBeats.Worker.SagaCoordinator;
using Serilog;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Configure file logging
_ = builder.ConfigureFileLogging( "SagaCoordinator" );

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

// Determine which providers are enabled based on configuration
HashSet<SupportedProviders> enabledProviders = [];
if (!string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:SpotifyClientId"] ) &&
    !string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:SpotifyClientSecret"] )) {
    _ = enabledProviders.Add( SupportedProviders.Spotify );
}
if (!string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:AppleTeamId"] ) &&
    !string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:AppleKeyId"] ) &&
    !string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:AppleKeyPath"] )) {
    _ = enabledProviders.Add( SupportedProviders.AppleMusic );
}
if (!string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:TidalClientId"] ) &&
    !string.IsNullOrWhiteSpace( builder.Configuration["BridgeBeats:TidalClientSecret"] )) {
    _ = enabledProviders.Add( SupportedProviders.Tidal );
}

// Register enabled providers as a singleton
_ = builder.Services.AddSingleton( enabledProviders );

// Register queue infrastructure services
_ = builder.Services.AddQueueInfrastructure( );

// Register all provider queues for secondary lookups
_ = builder.Services.AddAllProviderQueues<QueuedLookupRequest>( );

// Register ATProto session manager and storage service (centralized authentication)
_ = builder.Services.AddATProtoSessionManager( atProtoIdentifier, atProtoPassword );
_ = builder.Services.AddATProtoStorage( );

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
    sp.GetRequiredService<IProviderQueueResolver<QueuedLookupRequest>>( ),
    sp.GetRequiredService<HashSet<SupportedProviders>>( ),
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

try {
    app.Run( );
} finally {
    Log.CloseAndFlush( );
}
