using System.Diagnostics;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.CacheBootstrap.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Background service that bootstraps the Redis cache from ATProto public records.
/// Runs a full refresh on startup and periodically at the configured interval (default: every 6 hours).
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CacheBootstrapBackgroundService"/> class.
/// </remarks>
/// <param name="atProtoStorage">Service for accessing ATProto storage.</param>
/// <param name="cacheRepository">Service for caching media link results.</param>
/// <param name="redis">Redis connection for verification.</param>
/// <param name="settings">Configuration settings for the bootstrap service.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed partial class CacheBootstrapBackgroundService(
    IATProtoStorageService atProtoStorage,
    IMediaLinkCacheRepository cacheRepository,
    IConnectionMultiplexer redis,
    CacheBootstrapSettings settings,
    ILogger<CacheBootstrapBackgroundService> logger
) : BackgroundService {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        // Run bootstrap immediately on startup
        await RunBootstrapAsync( stoppingToken );

        // Then run periodically at the configured interval
        using PeriodicTimer timer = new( settings.BootstrapInterval );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                _ = await timer.WaitForNextTickAsync( stoppingToken );
                await RunBootstrapAsync( stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // Normal shutdown, exit gracefully
                break;
            } catch (Exception ex) {
                LogBootstrapPeriodicError( logger, ex );
            }
        }

        LogBootstrapShuttingDown( logger );
    }

    /// <summary>
    /// Performs a full cache bootstrap by fetching all records from ATProto and populating Redis.
    /// </summary>
    private async Task RunBootstrapAsync( CancellationToken cancellationToken ) {
        // Get Redis key count before bootstrap for comparison
        long keyCountBefore = 0;
        try {
            foreach (System.Net.EndPoint endpoint in redis.GetEndPoints( )) {
                IServer server = redis.GetServer( endpoint );
                keyCountBefore = server.DatabaseSize( );
                if (logger.IsEnabled( LogLevel.Information )) {
                    string endpointStr = endpoint.ToString( ) ?? "unknown";
                    LogRedisKeyCountBefore( logger, keyCountBefore, endpointStr );
                }
            }
        } catch (Exception ex) {
            LogRedisKeyCountBeforeError( logger, ex );
        }

        if (logger.IsEnabled( LogLevel.Information )) {
            string pdsUriStr = settings.PdsUri?.ToString( ) ?? "unknown";
            string userDidStr = settings.UserDid ?? "unknown";
            LogBootstrapStarting( logger, pdsUriStr, userDidStr );
        }

        Stopwatch stopwatch = Stopwatch.StartNew( );
        int successCount = 0;
        int errorCount = 0;

        // Validate settings before proceeding
        if (settings.PdsUri is null || settings.UserDid is null) {
            LogBootstrapFatalError( logger, new InvalidOperationException( "PdsUri and UserDid must be configured" ) );
            return;
        }

        try {
            await foreach ((string atUri, MediaLinkResult result) in
                atProtoStorage.ListAllRecordsAsync( settings.PdsUri, settings.UserDid, cancellationToken )) {
                try {
                    // Populate Redis cache indices for this record
                    await cacheRepository.AddInputLinksAsync( atUri, result );
                    successCount++;

                    if (successCount % 100 == 0) {
                        LogBootstrapProgress( logger, successCount );
                    }
                } catch (Exception ex) {
                    errorCount++;
                    LogCacheRecordError( logger, ex, atUri );
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            LogBootstrapCancelled( logger );
            throw;
        } catch (Exception ex) {
            LogBootstrapFatalError( logger, ex );
        }

        stopwatch.Stop( );

        // Get Redis key count after bootstrap
        long keyCountAfter = 0;
        try {
            foreach (System.Net.EndPoint endpoint in redis.GetEndPoints( )) {
                IServer server = redis.GetServer( endpoint );
                keyCountAfter = server.DatabaseSize( );
                if (logger.IsEnabled( LogLevel.Information )) {
                    string endpointStr = endpoint.ToString( ) ?? "unknown";
                    LogRedisKeyCountAfter( logger, keyCountAfter, endpointStr );
                }
            }
        } catch (Exception ex) {
            LogRedisKeyCountAfterError( logger, ex );
        }

        LogBootstrapCompleted(
            logger,
            stopwatch.Elapsed.TotalSeconds,
            successCount,
            errorCount,
            keyCountBefore,
            keyCountAfter,
            keyCountAfter - keyCountBefore
        );
    }

    #region LoggerMessage Methods

    /// <summary>Logs error during periodic cache bootstrap.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapPeriodicError,
        Level = LogLevel.Error,
        Message = "Error during periodic cache bootstrap, will retry at next interval" )]
    private static partial void LogBootstrapPeriodicError( ILogger logger, Exception ex );

    /// <summary>Logs that the cache bootstrap service is shutting down.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapShuttingDown,
        Level = LogLevel.Information,
        Message = "Cache bootstrap service is shutting down" )]
    private static partial void LogBootstrapShuttingDown( ILogger logger );

    /// <summary>Logs Redis key count before bootstrap.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountBefore,
        Level = LogLevel.Information,
        Message = "Redis key count BEFORE bootstrap: {KeyCount} (endpoint: {Endpoint})" )]
    private static partial void LogRedisKeyCountBefore( ILogger logger, long keyCount, string endpoint );

    /// <summary>Logs failure to get Redis key count before bootstrap.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountBeforeError,
        Level = LogLevel.Warning,
        Message = "Failed to get Redis key count before bootstrap" )]
    private static partial void LogRedisKeyCountBeforeError( ILogger logger, Exception ex );

    /// <summary>Logs that cache bootstrap is starting.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapStarting,
        Level = LogLevel.Information,
        Message = "Starting cache bootstrap from ATProto PDS: {PdsUri}, DID: {UserDid}" )]
    private static partial void LogBootstrapStarting( ILogger logger, string pdsUri, string userDid );

    /// <summary>Logs bootstrap progress.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapProgress,
        Level = LogLevel.Information,
        Message = "Bootstrap progress: {Count} records processed" )]
    private static partial void LogBootstrapProgress( ILogger logger, int count );

    /// <summary>Logs failure to cache a record.</summary>
    [LoggerMessage(
        EventId = LogEventIds.CacheRecordError,
        Level = LogLevel.Warning,
        Message = "Failed to cache record: {AtUri}" )]
    private static partial void LogCacheRecordError( ILogger logger, Exception ex, string atUri );

    /// <summary>Logs that cache bootstrap was cancelled.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapCancelled,
        Level = LogLevel.Information,
        Message = "Cache bootstrap was cancelled" )]
    private static partial void LogBootstrapCancelled( ILogger logger );

    /// <summary>Logs fatal error during cache bootstrap.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapFatalError,
        Level = LogLevel.Error,
        Message = "Fatal error during cache bootstrap" )]
    private static partial void LogBootstrapFatalError( ILogger logger, Exception ex );

    /// <summary>Logs Redis key count after bootstrap.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountAfter,
        Level = LogLevel.Information,
        Message = "Redis key count AFTER bootstrap: {KeyCount} (endpoint: {Endpoint})" )]
    private static partial void LogRedisKeyCountAfter( ILogger logger, long keyCount, string endpoint );

    /// <summary>Logs failure to get Redis key count after bootstrap.</summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountAfterError,
        Level = LogLevel.Warning,
        Message = "Failed to get Redis key count after bootstrap" )]
    private static partial void LogRedisKeyCountAfterError( ILogger logger, Exception ex );

    /// <summary>Logs cache bootstrap completion.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapCompleted,
        Level = LogLevel.Information,
        Message = "Cache bootstrap completed in {ElapsedSeconds:F1}s. Success: {SuccessCount}, Errors: {ErrorCount}, Keys before: {KeysBefore}, Keys after: {KeysAfter}, Net change: {NetChange}" )]
    private static partial void LogBootstrapCompleted(
        ILogger logger,
        double elapsedSeconds,
        int successCount,
        int errorCount,
        long keysBefore,
        long keysAfter,
        long netChange );

    #endregion
}
