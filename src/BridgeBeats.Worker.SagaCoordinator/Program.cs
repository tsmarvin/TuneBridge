using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Services.Queue;
using BridgeBeats.Core.Infrastructure.Cache;
using BridgeBeats.Core.Infrastructure.Extensions;
using Serilog;
using StackExchange.Redis;

namespace BridgeBeats.Worker.SagaCoordinator;

/// <summary>
/// Entry point for the SagaCoordinator worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the SagaCoordinator worker application.
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
    /// Configures the services for the SagaCoordinator worker application.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    private static void ConfigureServices( WebApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "SagaCoordinator" );

        // Add Aspire service defaults (health checks, telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Add Redis client from Aspire
        builder.AddRedisClient( "redis" );

        // Read and validate credentials
        (string atProtoIdentifier, string atProtoPassword, string atProtoUserDID, int cacheDays) =
            ValidateConfiguration( builder );

        // Determine which providers are enabled based on configuration
        HashSet<SupportedProviders> enabledProviders = DetectEnabledProviders(builder);

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
    }

    /// <summary>
    /// Validates the required configuration for the SagaCoordinator worker.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>A tuple containing the validated ATProto credentials and cache settings.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
    private static (string AtProtoIdentifier, string AtProtoPassword, string AtProtoUserDID, int CacheDays)
        ValidateConfiguration( WebApplicationBuilder builder ) {
        string? atProtoIdentifier = builder.Configuration["BridgeBeats:ATProtoIdentifier"];
        string? atProtoPassword = builder.Configuration["BridgeBeats:ATProtoPassword"];
        string? atProtoUserDID = builder.Configuration["BridgeBeats:ATProtoUserDID"];
        int cacheDays = builder.Configuration.GetValue("BridgeBeats:CacheDays", 30);

        return string.IsNullOrWhiteSpace( atProtoIdentifier ) ||
            string.IsNullOrWhiteSpace( atProtoPassword ) ||
            string.IsNullOrWhiteSpace( atProtoUserDID )
            ? throw new InvalidOperationException(
                "ATProto credentials are required. Set BridgeBeats:ATProtoIdentifier, " +
                "BridgeBeats:ATProtoPassword, and BridgeBeats:ATProtoUserDID."
            )
            : ((string AtProtoIdentifier, string AtProtoPassword, string AtProtoUserDID, int CacheDays))(atProtoIdentifier, atProtoPassword, atProtoUserDID, cacheDays);
    }

    /// <summary>
    /// Detects which music providers are enabled based on configuration.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>A set of enabled providers.</returns>
    private static HashSet<SupportedProviders> DetectEnabledProviders( WebApplicationBuilder builder ) {
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

        return enabledProviders;
    }

    /// <summary>
    /// Configures the endpoints for the SagaCoordinator worker application.
    /// </summary>
    /// <param name="app">The web application.</param>
    private static void ConfigureEndpoints( WebApplication app ) {
        // Map Aspire health check endpoints
        _ = app.MapDefaultEndpoints( );

        // Simple status endpoint
        _ = app.MapGet( "/status", ( ) => new {
            Service = "SagaCoordinator",
            Status = "Running",
            Timestamp = DateTimeOffset.UtcNow
        } );
    }
}
