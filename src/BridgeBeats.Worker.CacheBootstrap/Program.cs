using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Infrastructure.Cache;
using BridgeBeats.Core.Infrastructure.Extensions;
using Serilog;
using StackExchange.Redis;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Entry point for the CacheBootstrap worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the CacheBootstrap worker application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static async Task Main( string[] args ) {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        ConfigureServices( builder );

        WebApplication app = builder.Build();

        await ValidateRedisConnectionAsync( app );

        ConfigureEndpoints( app );

        try {
            app.Run( );
        } finally {
            Log.CloseAndFlush( );
        }
    }

    /// <summary>
    /// Configures the services for the CacheBootstrap worker application.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    private static void ConfigureServices( WebApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "CacheBootstrap" );

        // Add Aspire service defaults (health checks, telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire
        builder.AddRedisClient( "redis" );

        // Read and validate credentials
        (string atProtoIdentifier, string atProtoPassword, string atProtoUserDID,
            string atProtoPdsUri, int cacheDays, int bootstrapIntervalHours) =
                ValidateConfiguration( builder );

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
    }

    /// <summary>
    /// Validates the required configuration for the CacheBootstrap worker.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>A tuple containing the validated ATProto credentials and settings.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
    private static (string AtProtoIdentifier, string AtProtoPassword, string AtProtoUserDID,
        string AtProtoPdsUri, int CacheDays, int BootstrapIntervalHours) ValidateConfiguration(
            WebApplicationBuilder builder
    ) {
        string? atProtoIdentifier = builder.Configuration["BridgeBeats:ATProtoIdentifier"];
        string? atProtoPassword = builder.Configuration["BridgeBeats:ATProtoPassword"];
        string? atProtoUserDID = builder.Configuration["BridgeBeats:ATProtoUserDID"];
        string atProtoPdsUri = builder.Configuration["BridgeBeats:ATProtoPdsUri"]
            ?? "https://pds.bridgebeats.link";
        int cacheDays = builder.Configuration.GetValue("BridgeBeats:CacheDays", 30);
        int bootstrapIntervalHours = builder.Configuration.GetValue("BridgeBeats:BootstrapIntervalHours", 6);

        return string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
            string.IsNullOrWhiteSpace( atProtoPassword ) ||
            string.IsNullOrWhiteSpace( atProtoUserDID )
            ? throw new InvalidOperationException(
                "ATProto credentials are required. Set BridgeBeats:ATProtoIdentifier, " +
                "BridgeBeats:ATProtoPassword, and BridgeBeats:ATProtoUserDID."
            )
            : ((string AtProtoIdentifier, string AtProtoPassword, string AtProtoUserDID, string AtProtoPdsUri, int CacheDays, int BootstrapIntervalHours))(atProtoIdentifier, atProtoPassword, atProtoUserDID,
            atProtoPdsUri, cacheDays, bootstrapIntervalHours);
    }

    /// <summary>
    /// Validates the Redis connection on startup by performing test read/write operations.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static async Task ValidateRedisConnectionAsync( WebApplication app ) {
        IConnectionMultiplexer redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
        ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("CacheBootstrap.Startup");

        // Log Redis connection details
        if (logger.IsEnabled( LogLevel.Information )) {
            int databaseNum = redis.GetDatabase( ).Database;
            ProgramLog.LogRedisConnectionInfo(
                logger,
                redis.Configuration,
                redis.IsConnected,
                databaseNum
            );
        }

        // Verify we can actually write to Redis
        IDatabase db = redis.GetDatabase();
        string testKey = "cache-bootstrap:startup-test";
        bool setResult = await db.StringSetAsync(testKey, DateTimeOffset.UtcNow.ToString(), TimeSpan.FromMinutes(1));
        string? getValue = await db.StringGetAsync(testKey);
        ProgramLog.LogRedisWriteTest(
            logger,
            setResult,
            getValue
        );

        if (!setResult || string.IsNullOrEmpty( getValue )) {
            ProgramLog.LogRedisWriteVerificationFailed(
                logger,
                setResult,
                getValue
            );
        }

        // Log current key count for debugging
        foreach (System.Net.EndPoint endpoint in redis.GetEndPoints( )) {
            IServer server = redis.GetServer(endpoint);
            long keyCount = server.DatabaseSize();
            if (logger.IsEnabled( LogLevel.Information )) {
                string endpointStr = endpoint.ToString( ) ?? "unknown";
                ProgramLog.LogRedisKeyCount( logger, endpointStr, keyCount );
            }
        }
    }

    /// <summary>
    /// Configures the endpoints for the CacheBootstrap worker application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static void ConfigureEndpoints( WebApplication app ) {
        // Map Aspire health check endpoints
        _ = app.MapDefaultEndpoints( );

        // Simple status endpoint
        _ = app.MapGet( "/status", ( ) => new {
            Service = "CacheBootstrap",
            Status = "Running",
            Timestamp = DateTimeOffset.UtcNow
        } );
    }
}
