using System.Diagnostics;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Worker.CacheBootstrap.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Rebuilds the Redis lookup index from the ATProto PDS, which is the durable source of truth. Redis
/// is treated as a disposable cache: after a flush or restart it may be empty, and this service
/// re-hydrates it. It runs once at startup and then on a <see cref="System.Threading.PeriodicTimer"/>
/// driven by <see cref="CacheBootstrapSettings.BootstrapInterval"/>.
/// </summary>
/// <remarks>
/// Each run streams every <see cref="Contracts.DTOs.MediaLinkResult"/> record from the user's PDS via
/// <see cref="Contracts.Interfaces.IATProtoStorageService.ListAllRecordsAsync(System.Uri, string, System.Threading.CancellationToken, bool)"/>
/// and re-registers each record's input-link to record-URI pointers through
/// <see cref="Contracts.Interfaces.IMediaLinkCacheRepository.AddInputLinksAsync(string, Contracts.DTOs.MediaLinkResult)"/>.
/// Before-and-after Redis key counts are recorded, and a
/// <see cref="Contracts.DTOs.CacheBootstrapStatus"/> document is written to Redis (under
/// <see cref="Contracts.DTOs.CacheBootstrapStatus.RedisKey"/>, with a one-day TTL) so the Web layer
/// can surface bootstrap progress. A single record that fails to cache is logged and skipped; the run
/// continues.
/// </remarks>
/// <param name="atProtoStorage">Streams the user's records from the ATProto PDS.</param>
/// <param name="cacheRepository">Re-registers each record's input-link pointers into Redis.</param>
/// <param name="redis">The Redis connection used to read key counts and publish status.</param>
/// <param name="settings">The PDS, user DID, and run interval for the rebuild pass.</param>
/// <param name="logger">The logger for this service.</param>
public sealed partial class CacheBootstrapBackgroundService(
    IATProtoStorageService atProtoStorage,
    IMediaLinkCacheRepository cacheRepository,
    IConnectionMultiplexer redis,
    CacheBootstrapSettings settings,
    ILogger<CacheBootstrapBackgroundService> logger
) : BackgroundService {

    /// <summary>Serializer options used when writing the bootstrap status document to Redis.</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new( ) { WriteIndented = false };

    /// <summary>Serializer options used when reading the bootstrap status document back from Redis.</summary>
    private static readonly JsonSerializerOptions s_jsonReadOptions = new( ) { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Runs an immediate bootstrap pass, then repeats on the configured interval until the host stops.
    /// A failure in a periodic pass is logged and does not stop the loop; cancellation ends it cleanly.
    /// </summary>
    /// <param name="stoppingToken">Signals when the host is shutting down.</param>
    /// <returns>A task that completes when the service stops.</returns>
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
    /// Writes the current bootstrap status to Redis under
    /// <see cref="CacheBootstrapStatus.RedisKey"/> with a one-day TTL so the Web layer can surface it.
    /// Failures are logged and swallowed; status reporting never aborts a run.
    /// </summary>
    /// <param name="status">The status snapshot to publish.</param>
    /// <returns>A task that completes when the status has been written (or the write failed and was logged).</returns>
    private async Task UpdateStatusAsync( CacheBootstrapStatus status ) {
        try {
            IDatabase db = redis.GetDatabase( );
            string json = JsonSerializer.Serialize( status, s_jsonOptions );
            _ = await db.StringSetAsync( CacheBootstrapStatus.RedisKey, json, TimeSpan.FromDays( 1 ) );
        } catch (Exception ex) {
            LogStatusUpdateError( logger, ex );
        }
    }

    /// <summary>
    /// Reads the last-published bootstrap status from Redis so a new run can carry forward the prior
    /// run's counts while marking itself in progress. Failures are logged and treated as no status.
    /// </summary>
    /// <returns>
    /// The previously published <see cref="CacheBootstrapStatus"/>, or <see langword="null"/> if none
    /// exists or the read failed.
    /// </returns>
    private async Task<CacheBootstrapStatus?> GetCacheBootstrapStatusAsync( ) {
        try {
            IDatabase db = redis.GetDatabase( );
            RedisValue value = await db.StringGetAsync( CacheBootstrapStatus.RedisKey );
            return value.IsNullOrEmpty
                ? null
                : JsonSerializer.Deserialize<CacheBootstrapStatus>( value.ToString( ), s_jsonReadOptions );
        } catch (Exception ex) {
            LogStatusReadError( logger, ex );
            return null;
        }
    }

    /// <summary>
    /// Performs one full cache-rebuild pass: marks the status in progress, measures the Redis key count,
    /// streams every record from the PDS and re-registers its input-link pointers, then records timing,
    /// success/error counts, and the before/after key counts in the status document. A fatal error
    /// aborts the pass and is recorded; per-record failures are counted and skipped.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the pass.</param>
    /// <returns>A task that completes when the pass finishes or aborts.</returns>
    /// <exception cref="OperationCanceledException">
    /// Propagated when <paramref name="cancellationToken"/> is cancelled mid-pass.
    /// </exception>
    private async Task RunBootstrapAsync( CancellationToken cancellationToken ) {
        // Preserve previously completed-run fields when signalling that a run has started.
        CacheBootstrapStatus? previous = await GetCacheBootstrapStatusAsync( );
        await UpdateStatusAsync( new CacheBootstrapStatus {
            IsRunning = true,
            NextScheduledRun = null,
            LastRunTime = previous?.LastRunTime,
            LastSuccessCount = previous?.LastSuccessCount,
            LastErrorCount = previous?.LastErrorCount,
            LastDurationSeconds = previous?.LastDurationSeconds,
            RedisKeyCount = previous?.RedisKeyCount
        } );

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
            string pdsUriStr = settings.PdsUri.ToString( );
            string userDidStr = settings.UserDid;
            LogBootstrapStarting( logger, pdsUriStr, userDidStr );
        }

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
            stopwatch.Stop( );

            // Preserve the last successful run's fields; only update lifecycle fields.
            // Do NOT fall through to the normal completion write — that would record a zero-count
            // run as a completed run and clobber the previous successful run's stats.
            CacheBootstrapStatus? prior = await GetCacheBootstrapStatusAsync( );
            await UpdateStatusAsync( new CacheBootstrapStatus {
                IsRunning = false,
                NextScheduledRun = DateTimeOffset.UtcNow.Add( settings.BootstrapInterval ),
                LastRunTime = prior?.LastRunTime,
                LastSuccessCount = prior?.LastSuccessCount,
                LastErrorCount = prior?.LastErrorCount,
                LastDurationSeconds = prior?.LastDurationSeconds,
                RedisKeyCount = prior?.RedisKeyCount
            } );
            return;
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

        // Update status with completed run information
        await UpdateStatusAsync( new CacheBootstrapStatus {
            IsRunning = false,
            LastRunTime = DateTimeOffset.UtcNow,
            NextScheduledRun = DateTimeOffset.UtcNow.Add( settings.BootstrapInterval ),
            LastSuccessCount = successCount,
            LastErrorCount = errorCount,
            LastDurationSeconds = stopwatch.Elapsed.TotalSeconds,
            RedisKeyCount = keyCountAfter
        } );
    }

    #region LoggerMessage Methods

    /// <summary>Logs that a periodic bootstrap run failed and will retry at the next interval.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapPeriodicError,
        Level = LogLevel.Error,
        Message = "Error during periodic cache bootstrap, will retry at next interval" )]
    private static partial void LogBootstrapPeriodicError( ILogger logger, Exception ex );

    /// <summary>Logs that the bootstrap service is shutting down.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapShuttingDown,
        Level = LogLevel.Information,
        Message = "Cache bootstrap service is shutting down" )]
    private static partial void LogBootstrapShuttingDown( ILogger logger );

    /// <summary>Logs the Redis key count measured before a bootstrap run.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="keyCount">The number of keys present before the run.</param>
    /// <param name="endpoint">The Redis endpoint that was measured.</param>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountBefore,
        Level = LogLevel.Information,
        Message = "Redis key count BEFORE bootstrap: {KeyCount} (endpoint: {Endpoint})" )]
    private static partial void LogRedisKeyCountBefore( ILogger logger, long keyCount, string endpoint );

    /// <summary>Logs that measuring the Redis key count before a run failed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountBeforeError,
        Level = LogLevel.Warning,
        Message = "Failed to get Redis key count before bootstrap" )]
    private static partial void LogRedisKeyCountBeforeError( ILogger logger, Exception ex );

    /// <summary>Logs that a bootstrap run is starting against the configured PDS and DID.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="pdsUri">The PDS URI being read from.</param>
    /// <param name="userDid">The user DID whose records are rebuilt.</param>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapStarting,
        Level = LogLevel.Information,
        Message = "Starting cache bootstrap from ATProto PDS: {PdsUri}, DID: {UserDid}" )]
    private static partial void LogBootstrapStarting( ILogger logger, string pdsUri, string userDid );

    /// <summary>Logs a progress update reporting how many records have been processed so far.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of records processed.</param>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapProgress,
        Level = LogLevel.Information,
        Message = "Bootstrap progress: {Count} records processed" )]
    private static partial void LogBootstrapProgress( ILogger logger, int count );

    /// <summary>Logs that caching a single PDS record failed; the run continues with the next record.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    /// <param name="atUri">The AT-URI of the record that failed to cache.</param>
    [LoggerMessage(
        EventId = LogEventIds.CacheRecordError,
        Level = LogLevel.Warning,
        Message = "Failed to cache record: {AtUri}" )]
    private static partial void LogCacheRecordError( ILogger logger, Exception ex, string atUri );

    /// <summary>Logs that the bootstrap run was cancelled by host shutdown.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapCancelled,
        Level = LogLevel.Information,
        Message = "Cache bootstrap was cancelled" )]
    private static partial void LogBootstrapCancelled( ILogger logger );

    /// <summary>Logs a fatal error that aborted the bootstrap run.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    [LoggerMessage(
        EventId = LogEventIds.BootstrapFatalError,
        Level = LogLevel.Error,
        Message = "Fatal error during cache bootstrap" )]
    private static partial void LogBootstrapFatalError( ILogger logger, Exception ex );

    /// <summary>Logs the Redis key count measured after a bootstrap run.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="keyCount">The number of keys present after the run.</param>
    /// <param name="endpoint">The Redis endpoint that was measured.</param>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountAfter,
        Level = LogLevel.Information,
        Message = "Redis key count AFTER bootstrap: {KeyCount} (endpoint: {Endpoint})" )]
    private static partial void LogRedisKeyCountAfter( ILogger logger, long keyCount, string endpoint );

    /// <summary>Logs that measuring the Redis key count after a run failed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCountAfterError,
        Level = LogLevel.Warning,
        Message = "Failed to get Redis key count after bootstrap" )]
    private static partial void LogRedisKeyCountAfterError( ILogger logger, Exception ex );

    /// <summary>Logs that a bootstrap run completed, with timing, counts, and the key-count delta.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="elapsedSeconds">The run duration in seconds.</param>
    /// <param name="successCount">The number of records cached successfully.</param>
    /// <param name="errorCount">The number of records that failed to cache.</param>
    /// <param name="keysBefore">The Redis key count before the run.</param>
    /// <param name="keysAfter">The Redis key count after the run.</param>
    /// <param name="netChange">The net change in Redis key count across the run.</param>
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

    /// <summary>Logs that writing the bootstrap status document to Redis failed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    [LoggerMessage(
        EventId = LogEventIds.StatusUpdateError,
        Level = LogLevel.Warning,
        Message = "Failed to update cache bootstrap status in Redis" )]
    private static partial void LogStatusUpdateError( ILogger logger, Exception ex );

    /// <summary>Logs that reading the bootstrap status document from Redis failed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that was thrown.</param>
    [LoggerMessage(
        EventId = LogEventIds.StatusReadError,
        Level = LogLevel.Warning,
        Message = "Failed to read cache bootstrap status from Redis" )]
    private static partial void LogStatusReadError( ILogger logger, Exception ex );

    #endregion
}
