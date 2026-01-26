using System.Diagnostics;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
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
public sealed class CacheBootstrapBackgroundService(
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
                logger.LogError( ex, "Error during periodic cache bootstrap, will retry at next interval" );
            }
        }

        logger.LogInformation( "Cache bootstrap service is shutting down" );
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
                logger.LogInformation( "Redis key count BEFORE bootstrap: {KeyCount} (endpoint: {Endpoint})", keyCountBefore, endpoint );
            }
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to get Redis key count before bootstrap" );
        }

        logger.LogInformation(
            "Starting cache bootstrap from ATProto PDS: {PdsUri}, DID: {UserDid}",
            settings.PdsUri,
            settings.UserDid
        );

        Stopwatch stopwatch = Stopwatch.StartNew( );
        int successCount = 0;
        int errorCount = 0;

        try {
            await foreach ((string atUri, MediaLinkResult result) in
                atProtoStorage.ListAllRecordsAsync( settings.PdsUri, settings.UserDid, cancellationToken )) {
                try {
                    // Populate Redis cache indices for this record
                    await cacheRepository.AddInputLinksAsync( atUri, result );
                    successCount++;

                    if (successCount % 100 == 0) {
                        logger.LogInformation( "Bootstrap progress: {Count} records processed", successCount );
                    }
                } catch (Exception ex) {
                    errorCount++;
                    logger.LogWarning( ex, "Failed to cache record: {AtUri}", atUri );
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            logger.LogInformation( "Cache bootstrap was cancelled" );
            throw;
        } catch (Exception ex) {
            logger.LogError( ex, "Fatal error during cache bootstrap" );
        }

        stopwatch.Stop( );

        // Get Redis key count after bootstrap
        long keyCountAfter = 0;
        try {
            foreach (System.Net.EndPoint endpoint in redis.GetEndPoints( )) {
                IServer server = redis.GetServer( endpoint );
                keyCountAfter = server.DatabaseSize( );
                logger.LogInformation( "Redis key count AFTER bootstrap: {KeyCount} (endpoint: {Endpoint})", keyCountAfter, endpoint );
            }
        } catch (Exception ex) {
            logger.LogWarning( ex, "Failed to get Redis key count after bootstrap" );
        }

        logger.LogInformation(
            "Cache bootstrap completed in {ElapsedSeconds:F1}s. Success: {SuccessCount}, Errors: {ErrorCount}, Keys before: {KeysBefore}, Keys after: {KeysAfter}, Net change: {NetChange}",
            stopwatch.Elapsed.TotalSeconds,
            successCount,
            errorCount,
            keyCountBefore,
            keyCountAfter,
            keyCountAfter - keyCountBefore
        );
    }
}
