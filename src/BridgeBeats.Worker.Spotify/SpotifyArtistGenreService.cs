using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Worker.Spotify.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Background service that periodically refreshes Spotify artist-to-genre data into the genre cache.
/// </summary>
/// <remarks>
/// The service waits 30 seconds after startup, then loops on a five-minute check interval. The
/// "once every seven days" schedule is enforced by a local <c>lastFullRun</c> variable initialized
/// to <see cref="DateTimeOffset.MinValue"/> on every entry into <c>ExecuteAsync</c>. This means a
/// full run fires approximately 30 seconds after every process restart, not once per seven days
/// in-process. The seven-day guard only suppresses subsequent runs within the same process lifetime.
/// Errors in the loop are logged and retried after a one-minute delay rather than crashing the host.
/// </remarks>
/// <param name="redis">The Redis connection multiplexer (held for parity with other queue services; queue access is via <paramref name="genreCache"/>).</param>
/// <param name="genreCache">Cache service used to read the artist-refresh queue and store resolved genres.</param>
/// <param name="lookupService">Spotify lookup service used to resolve artist genres by id.</param>
/// <param name="logger">Logger for the service's structured log events.</param>
public sealed partial class SpotifyArtistGenreService(
    IConnectionMultiplexer redis,
    IGenreCacheService genreCache,
    SpotifyLookupService lookupService,
    ILogger<SpotifyArtistGenreService> logger
) : BackgroundService {

    /// <summary>Redis connection multiplexer; retained for parity with other queue services.</summary>
    private readonly IConnectionMultiplexer _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );

    /// <summary>Cache service used to drain the artist-refresh queue and persist resolved genres.</summary>
    private readonly IGenreCacheService _genreCache = genreCache
                                                    ?? throw new ArgumentNullException( nameof( genreCache ) );

    /// <summary>Spotify lookup service used to resolve artist genres by id.</summary>
    private readonly SpotifyLookupService _lookupService = lookupService
                                                         ?? throw new ArgumentNullException( nameof( lookupService ) );

    /// <summary>Logger for this service's structured log events.</summary>
    private readonly ILogger<SpotifyArtistGenreService> _logger = logger
                                                                ?? throw new ArgumentNullException( nameof( logger ) );

    /// <summary>How often the loop wakes to decide whether a scheduled run is due (5 minutes).</summary>
    private static readonly TimeSpan s_checkInterval = TimeSpan.FromMinutes( 5 );

    /// <summary>Minimum interval between full refresh runs (7 days).</summary>
    private static readonly TimeSpan s_scheduleInterval = TimeSpan.FromDays( 7 );

    /// <summary>Delay applied after an unhandled loop error before retrying (1 minute).</summary>
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromMinutes( 1 );

    /// <summary>
    /// Runs the refresh loop until cancellation: delays 30 seconds at startup, then on each
    /// five-minute check interval performs a full artist-genre refresh when seven days have
    /// elapsed since the last full run. Because <c>lastFullRun</c> is initialized to
    /// <see cref="DateTimeOffset.MinValue"/> on every call, the first full run fires
    /// approximately 30 seconds after every process restart regardless of when the previous
    /// run occurred.
    /// </summary>
    /// <param name="stoppingToken">Token signaled when the host is shutting down.</param>
    /// <returns>A task that completes when the loop exits.</returns>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogServiceStarting( _logger );

        // Wait a bit before starting to allow other services to initialize
        await Task.Delay( TimeSpan.FromSeconds( 30 ), stoppingToken );

        DateTimeOffset lastFullRun = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                // Check if it's time for a full scheduled run
                bool shouldRunScheduled = DateTimeOffset.UtcNow - lastFullRun >= s_scheduleInterval;

                if (shouldRunScheduled) {
                    LogStartingScheduledRefresh( _logger );
                    await ProcessAllQueuedArtistsAsync( stoppingToken );
                    lastFullRun = DateTimeOffset.UtcNow;
                    LogCompletedScheduledRefresh( _logger );
                }

                // Wait before checking again
                await Task.Delay( s_checkInterval, stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogProcessorLoopError( _logger, ex );
                await Task.Delay( s_errorDelay, stoppingToken );
            }
        }

        LogServiceStopping( _logger );
    }

    /// <summary>
    /// Drains the entire Spotify artist-refresh queue in batches, refreshing each batch's genres,
    /// until the queue is empty or cancellation is requested.
    /// </summary>
    /// <param name="ct">Token used to stop draining early.</param>
    /// <returns>A task that completes when the queue is empty or draining is cancelled.</returns>
    private async Task ProcessAllQueuedArtistsAsync( CancellationToken ct ) {
        long queueLength = await _genreCache.GetArtistRefreshQueueLengthAsync( SupportedProviders.Spotify, ct );
        LogQueueLength( _logger, queueLength );

        int totalProcessed = 0;
        int batchCount = 0;

        while (!ct.IsCancellationRequested) {
            IReadOnlyList<string> artistIds = await _genreCache.DequeueArtistsForRefreshAsync(
                SupportedProviders.Spotify,
                SpotifyConstants.MaxArtistsPerBatchLookup,
                ct
            );

            if (artistIds.Count == 0) {
                LogQueueEmpty( _logger );
                break;
            }

            batchCount++;
            int processed = await ProcessArtistBatchAsync( artistIds, ct );
            totalProcessed += processed;

            LogBatchProcessed( _logger, batchCount, processed, artistIds.Count );

            // Small delay between batches to be nice to Spotify's rate limits
            await Task.Delay( TimeSpan.FromMilliseconds( 100 ), ct );
        }

        LogRefreshComplete( _logger, totalProcessed, batchCount );
    }

    /// <summary>
    /// Resolves genres for a single batch of artist ids and stores the results in the genre cache.
    /// </summary>
    /// <param name="artistIds">The artist ids to refresh.</param>
    /// <param name="ct">Token used to cancel cache writes.</param>
    /// <returns>The number of artists whose genres were resolved and stored; artists not found are skipped.</returns>
    /// <remarks>
    /// Exceptions from the lookup or cache calls are caught and logged, and the method returns
    /// zero so the surrounding drain loop can continue with the next batch.
    /// </remarks>
    private async Task<int> ProcessArtistBatchAsync( IReadOnlyList<string> artistIds, CancellationToken ct ) {
        try {
            Dictionary<string, List<string>?> results = await _lookupService.GetArtistsByIdsAsync( artistIds );

            int successCount = 0;
            foreach ((string artistId, List<string>? genres) in results) {
                if (genres == null) {
                    // Artist not found - don't cache anything (leave for retry later if needed)
                    LogArtistNotFound( _logger, artistId );
                    continue;
                }

                // Cache the artist genres (empty list is valid - means artist has no genres)
                await _genreCache.SetArtistGenresAsync( SupportedProviders.Spotify, artistId, genres, ct );
                successCount++;
            }

            return successCount;
        } catch (Exception ex) {
            LogBatchError( _logger, ex, artistIds.Count );
            return 0;
        }
    }

    #region LoggerMessage Methods

    /// <summary>Logs that the artist-genre service has started.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistGenreServiceStarting,
        Level = LogLevel.Information,
        Message = "Spotify artist genre service starting" )]
    private static partial void LogServiceStarting( ILogger logger );

    /// <summary>Logs that a scheduled artist-genre refresh run is beginning.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.StartingScheduledRefresh,
        Level = LogLevel.Information,
        Message = "Starting scheduled artist genre refresh" )]
    private static partial void LogStartingScheduledRefresh( ILogger logger );

    /// <summary>Logs that a scheduled artist-genre refresh run has completed.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.CompletedScheduledRefresh,
        Level = LogLevel.Information,
        Message = "Completed scheduled artist genre refresh" )]
    private static partial void LogCompletedScheduledRefresh( ILogger logger );

    /// <summary>Logs an unhandled error in the artist-genre processor loop.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistProcessorLoopError,
        Level = LogLevel.Error,
        Message = "Error in artist genre processor loop" )]
    private static partial void LogProcessorLoopError( ILogger logger, Exception ex );

    /// <summary>Logs that the artist-genre service is stopping.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistGenreServiceStopping,
        Level = LogLevel.Information,
        Message = "Spotify artist genre service stopping" )]
    private static partial void LogServiceStopping( ILogger logger );

    /// <summary>Logs the current depth of the artist-refresh queue.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="queueLength">The number of artists currently pending refresh.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistQueueLength,
        Level = LogLevel.Information,
        Message = "Artist refresh queue has {QueueLength} artists pending" )]
    private static partial void LogQueueLength( ILogger logger, long queueLength );

    /// <summary>Logs that the artist-refresh queue drained empty.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistQueueEmpty,
        Level = LogLevel.Debug,
        Message = "Artist refresh queue is empty" )]
    private static partial void LogQueueEmpty( ILogger logger );

    /// <summary>Logs the outcome of processing a single artist batch.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="batchNumber">The one-based index of the batch within the run.</param>
    /// <param name="processed">The number of artists successfully refreshed in the batch.</param>
    /// <param name="total">The number of artists in the batch.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistBatchProcessed,
        Level = LogLevel.Debug,
        Message = "Batch {BatchNumber}: Processed {Processed}/{Total} artists" )]
    private static partial void LogBatchProcessed( ILogger logger, int batchNumber, int processed, int total );

    /// <summary>Logs that an artist-genre refresh run completed, with totals.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="totalProcessed">The total number of artists refreshed across the run.</param>
    /// <param name="batchCount">The number of batches processed in the run.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistRefreshComplete,
        Level = LogLevel.Information,
        Message = "Artist genre refresh complete: {TotalProcessed} artists in {BatchCount} batches" )]
    private static partial void LogRefreshComplete( ILogger logger, int totalProcessed, int batchCount );

    /// <summary>Logs that a requested artist id was not found.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="artistId">The artist id that was not found.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistNotFound,
        Level = LogLevel.Debug,
        Message = "Artist {ArtistId} not found" )]
    private static partial void LogArtistNotFound( ILogger logger, string artistId );

    /// <summary>Logs an error that occurred while processing an artist batch.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="count">The number of artists in the failed batch.</param>
    [LoggerMessage(
        EventId = LogEventIds.ArtistBatchError,
        Level = LogLevel.Error,
        Message = "Error processing artist batch of {Count} artists" )]
    private static partial void LogBatchError( ILogger logger, Exception ex, int count );

    #endregion
}
