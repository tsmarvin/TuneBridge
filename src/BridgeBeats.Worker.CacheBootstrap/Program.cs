using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Storage;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Worker.CacheBootstrap;
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
string? atProtoPdsUri = builder.Configuration["BridgeBeats:ATProtoPdsUri"] ?? "https://pds.bridgebeats.link";
int cacheDays = builder.Configuration.GetValue( "BridgeBeats:CacheDays", 30 );
int bootstrapIntervalHours = builder.Configuration.GetValue( "BridgeBeats:BootstrapIntervalHours", 6 );

// Validate ATProto credentials
if (string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
    string.IsNullOrWhiteSpace( atProtoPassword ) ||
    string.IsNullOrWhiteSpace( atProtoUserDID )) {
    throw new InvalidOperationException(
        "ATProto credentials are required. Set BridgeBeats:ATProtoIdentifier, BridgeBeats:ATProtoPassword, and BridgeBeats:ATProtoUserDID."
    );
}

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

// Register the cache bootstrap background service with configuration
_ = builder.Services.AddSingleton( new CacheBootstrapSettings(
    new Uri( atProtoPdsUri ),
    atProtoUserDID,
    TimeSpan.FromHours( bootstrapIntervalHours )
) );
_ = builder.Services.AddHostedService<CacheBootstrapBackgroundService>( );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

// Simple status endpoint
app.MapGet( "/status", ( ) => new {
    Service = "CacheBootstrap",
    Status = "Running",
    Timestamp = DateTimeOffset.UtcNow
} );

app.Run( );
