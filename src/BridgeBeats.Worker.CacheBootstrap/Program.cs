using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Infrastructure.Storage;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Worker.CacheBootstrap;
using Serilog;
using StackExchange.Redis;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Configure file logging
_ = builder.ConfigureFileLogging( "CacheBootstrap" );

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

// Validate Redis connection on startup
IConnectionMultiplexer redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();

// Log Redis connection details
logger.LogInformation(
    "Redis connection: {Configuration}, IsConnected: {IsConnected}, Database: {Database}",
    redis.Configuration,
    redis.IsConnected,
    redis.GetDatabase( ).Database
);

// Verify we can actually write to Redis
IDatabase db = redis.GetDatabase();
string testKey = "cache-bootstrap:startup-test";
bool setResult = await db.StringSetAsync(testKey, DateTimeOffset.UtcNow.ToString(), TimeSpan.FromMinutes(1));
string? getValue = await db.StringGetAsync(testKey);
logger.LogInformation(
    "Redis write test - SetResult: {SetResult}, ReadBack: {ReadBack}",
    setResult,
    getValue
);

if (!setResult || string.IsNullOrEmpty( getValue )) {
    logger.LogError( "Redis write verification failed! SetResult: {SetResult}, ReadBack: {ReadBack}", setResult, getValue );
}

// Log current key count for debugging
long keyCount = 0;
foreach (System.Net.EndPoint endpoint in redis.GetEndPoints( )) {
    IServer server = redis.GetServer(endpoint);
    keyCount = server.DatabaseSize( );
    logger.LogInformation( "Redis server {Endpoint} has {KeyCount} keys", endpoint, keyCount );
}

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

// Simple status endpoint
app.MapGet( "/status", ( ) => new {
    Service = "CacheBootstrap",
    Status = "Running",
    Timestamp = DateTimeOffset.UtcNow
} );

try {
    app.Run( );
} finally {
    Log.CloseAndFlush( );
}
