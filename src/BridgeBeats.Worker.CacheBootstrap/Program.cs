using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Infrastructure.Cache;
using BridgeBeats.Core.Infrastructure.Extensions;
using BridgeBeats.Core.Infrastructure.Storage;
using Serilog;
using StackExchange.Redis;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Entry point and composition root for the CacheBootstrap worker process. The worker is a headless
/// background host (no HTTP endpoint): it builds a generic host, registers the Redis client, ATProto
/// storage, the media-link cache, and the <see cref="CacheBootstrapBackgroundService"/> that rebuilds
/// the Redis lookup index from the PDS.
/// </summary>
public static class Program {

    /// <summary>
    /// Builds the host, verifies Redis is reachable with a write/read-back smoke test, and runs the
    /// worker. Configuration is validated during service registration so missing credentials surface
    /// at startup.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to the host builder.</param>
    /// <returns>A task that completes when the host has stopped.</returns>
    public static async Task Main( string[] args ) {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder( args );

        ConfigureServices( builder );

        IHost app = builder.Build();

        await ValidateRedisConnectionAsync( app );

        try {
            app.Run( );
        } finally {
            Log.CloseAndFlush( );
        }
    }

    /// <summary>
    /// Registers the worker's services: file logging, Aspire service defaults, the Redis client, the
    /// ATProto session and storage, the media-link cache, the <see cref="CacheBootstrapSettings"/>
    /// built from configuration, and the hosted <see cref="CacheBootstrapBackgroundService"/>.
    /// Validates configuration before wiring dependent services.
    /// </summary>
    /// <param name="builder">The host application builder being configured.</param>
    private static void ConfigureServices( HostApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "CacheBootstrap" );

        // Add Aspire service defaults (telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire
        builder.AddRedisClient( "redis" );

        // Lane B: Data Protection — must use the same app name and key path as Web so
        // values encrypted in one process (Web, SagaCoordinator, CacheBootstrap) can be
        // decrypted by any other.
        string dataProtectionKeyPath = builder.Configuration["BridgeBeats:DataProtectionKeyPath"] ?? DataProtectionExtensions.DefaultKeyPath;
        _ = builder.Services.AddBridgeBeatsDataProtection( dataProtectionKeyPath );

        // Read and validate credentials
        (string atProtoIdentifier, string atProtoPassword, string atProtoUserDID,
            string atProtoPdsUri, int cacheDays, int bootstrapIntervalHours) =
                ValidateConfiguration( builder );

        // Register ATProto session manager and storage service (centralized authentication)
        int sessionTtlDays = builder.Configuration.GetValue( "BridgeBeats:ATProtoSessionTtlDays", 45 );
        _ = builder.Services.AddATProtoSessionManager( atProtoIdentifier, atProtoPassword, sessionTtlDays );
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
    /// Reads and validates the ATProto credentials and bootstrap settings from configuration. The PDS
    /// URI, cache-retention days, and bootstrap interval fall back to defaults when unset; the user DID
    /// is validated with
    /// <see cref="Core.Infrastructure.Storage.ATProtoUriHelper.ValidateDid(string, string)"/>.
    /// </summary>
    /// <param name="builder">The host application builder whose configuration is read.</param>
    /// <returns>
    /// A tuple of the ATProto identifier, app password, user DID, PDS URI, cache-retention days
    /// (default 30), and bootstrap interval in hours (default 6).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any of the required ATProto credentials are missing or blank.
    /// </exception>
    private static (string AtProtoIdentifier, string AtProtoPassword, string AtProtoUserDID,
        string AtProtoPdsUri, int CacheDays, int BootstrapIntervalHours) ValidateConfiguration(
            HostApplicationBuilder builder
    ) {
        string? atProtoIdentifier = builder.Configuration["BridgeBeats:ATProtoIdentifier"];
        string? atProtoPassword = builder.Configuration["BridgeBeats:ATProtoPassword"];
        string? atProtoUserDID = builder.Configuration["BridgeBeats:ATProtoUserDID"];
        string atProtoPdsUri = builder.Configuration["BridgeBeats:ATProtoPdsUri"]
            ?? "https://pds.bridgebeats.link";
        int cacheDays = builder.Configuration.GetValue("BridgeBeats:CacheDays", 30);
        int bootstrapIntervalHours = builder.Configuration.GetValue("BridgeBeats:BootstrapIntervalHours", 6);

        if (string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
            string.IsNullOrWhiteSpace( atProtoPassword ) ||
            string.IsNullOrWhiteSpace( atProtoUserDID )) {
            throw new InvalidOperationException(
                "ATProto credentials are required. Set BridgeBeats:ATProtoIdentifier, " +
                "BridgeBeats:ATProtoPassword, and BridgeBeats:ATProtoUserDID."
            );
        }

        // Validate DID format (must start with did:plc: or did:web:)
        ATProtoUriHelper.ValidateDid( atProtoUserDID, "BridgeBeats:ATProtoUserDID" );

        return (atProtoIdentifier, atProtoPassword, atProtoUserDID, atProtoPdsUri, cacheDays, bootstrapIntervalHours);
    }

    /// <summary>
    /// Verifies the Redis connection at startup by logging connection details, writing a short-lived
    /// test key and reading it back, and reporting the key count per endpoint. A failed write/read-back
    /// is logged as an error but does not abort startup.
    /// </summary>
    /// <param name="app">The built host whose Redis client and logger factory are resolved.</param>
    /// <returns>A task that completes when the verification steps have run.</returns>
    private static async Task ValidateRedisConnectionAsync( IHost app ) {
        IConnectionMultiplexer redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
        ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger("CacheBootstrap.Startup");

        // Log Redis connection details
        if (logger.IsEnabled( LogLevel.Information )) {
            int databaseNum = redis.GetDatabase( ).Database;
            string sanitizedEndpoints = SanitizeRedisConfiguration( redis.Configuration );
            ProgramLog.LogRedisConnectionInfo(
                logger,
                sanitizedEndpoints,
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
    /// Returns a sanitized representation of a Redis connection string with the password redacted.
    /// Parses the configuration via <see cref="ConfigurationOptions"/> and renders it with
    /// <c>includePassword: false</c> so the password field is replaced with asterisks. Returns
    /// <c>"(unavailable)"</c> when <paramref name="configuration"/> is <see langword="null"/> or empty
    /// to avoid calling <see cref="ConfigurationOptions.Parse(string)"/> on an empty string, which throws.
    /// </summary>
    /// <param name="configuration">The raw Redis connection string to sanitize, or <see langword="null"/>.</param>
    /// <returns>
    /// A connection string with the password replaced by <c>*****</c>, or <c>"(unavailable)"</c> if
    /// <paramref name="configuration"/> is <see langword="null"/> or empty.
    /// </returns>
    internal static string SanitizeRedisConfiguration( string? configuration ) {
        if (string.IsNullOrEmpty( configuration )) {
            return "(unavailable)";
        }

        return ConfigurationOptions.Parse( configuration ).ToString( includePassword: false );
    }
}
