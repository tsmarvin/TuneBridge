using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.Spotify;
using BridgeBeats.Worker.Spotify.Logging;
using StackExchange.Redis;

namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Background service that processes artist genre lookups for Spotify on a schedule.
/// </summary>
/// <remarks>
/// <para>
/// This service runs on a configurable schedule (default: weekly on Sunday at midnight)
/// to batch-process artist genre lookups. Artists are queued during track lookups
/// and processed in batches of 50 (Spotify's API limit).
/// </para>
/// <para>
/// The service:
/// <list type="bullet">
///   <item>Dequeues up to 50 artists from the refresh queue</item>
///   <item>Calls Spotify bulk artists API to get genres</item>
///   <item>Caches artist genres for future track genre resolution</item>
/// </list>
/// </para>
/// </remarks>
/// <remarks>
/// Initializes a new instance of the <see cref="SpotifyArtistGenreService"/> class.
/// </remarks>
public sealed partial class SpotifyArtistGenreService(
    IConnectionMultiplexer redis,
    IGenreCacheService genreCache,
    SpotifyLookupService lookupService,
    ILogger<SpotifyArtistGenreService> logger
) : BackgroundService {

    private readonly IConnectionMultiplexer _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
    private readonly IGenreCacheService _genreCache = genreCache
                                                    ?? throw new ArgumentNullException( nameof( genreCache ) );
    private readonly SpotifyLookupService _lookupService = lookupService
                                                         ?? throw new ArgumentNullException( nameof( lookupService ) );
    private readonly ILogger<SpotifyArtistGenreService> _logger = logger
                                                                ?? throw new ArgumentNullException( nameof( logger ) );

    private static readonly TimeSpan s_checkInterval = TimeSpan.FromMinutes( 5 );
    private static readonly TimeSpan s_scheduleInterval = TimeSpan.FromDays( 7 );
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromMinutes( 1 );

    /// <inheritdoc/>
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
    /// Processes all queued artists in batches until the queue is empty.
    /// </summary>
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
    /// Processes a batch of artist IDs to fetch and cache their genres.
    /// </summary>
    /// <param name="artistIds">The artist IDs to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of artists successfully processed.</returns>
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

    /// <summary>Logs that the service is starting.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistGenreServiceStarting,
        Level = LogLevel.Information,
        Message = "Spotify artist genre service starting" )]
    private static partial void LogServiceStarting( ILogger logger );

    /// <summary>Logs that a scheduled refresh is starting.</summary>
    [LoggerMessage(
        EventId = LogEventIds.StartingScheduledRefresh,
        Level = LogLevel.Information,
        Message = "Starting scheduled artist genre refresh" )]
    private static partial void LogStartingScheduledRefresh( ILogger logger );

    /// <summary>Logs that a scheduled refresh completed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.CompletedScheduledRefresh,
        Level = LogLevel.Information,
        Message = "Completed scheduled artist genre refresh" )]
    private static partial void LogCompletedScheduledRefresh( ILogger logger );

    /// <summary>Logs an error in the processor loop.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistProcessorLoopError,
        Level = LogLevel.Error,
        Message = "Error in artist genre processor loop" )]
    private static partial void LogProcessorLoopError( ILogger logger, Exception ex );

    /// <summary>Logs that the service is stopping.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistGenreServiceStopping,
        Level = LogLevel.Information,
        Message = "Spotify artist genre service stopping" )]
    private static partial void LogServiceStopping( ILogger logger );

    /// <summary>Logs the current queue length.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistQueueLength,
        Level = LogLevel.Information,
        Message = "Artist refresh queue has {QueueLength} artists pending" )]
    private static partial void LogQueueLength( ILogger logger, long queueLength );

    /// <summary>Logs that the queue is empty.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistQueueEmpty,
        Level = LogLevel.Debug,
        Message = "Artist refresh queue is empty" )]
    private static partial void LogQueueEmpty( ILogger logger );

    /// <summary>Logs that a batch was processed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistBatchProcessed,
        Level = LogLevel.Debug,
        Message = "Batch {BatchNumber}: Processed {Processed}/{Total} artists" )]
    private static partial void LogBatchProcessed( ILogger logger, int batchNumber, int processed, int total );

    /// <summary>Logs that the refresh is complete.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistRefreshComplete,
        Level = LogLevel.Information,
        Message = "Artist genre refresh complete: {TotalProcessed} artists in {BatchCount} batches" )]
    private static partial void LogRefreshComplete( ILogger logger, int totalProcessed, int batchCount );

    /// <summary>Logs that an artist was not found.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistNotFound,
        Level = LogLevel.Debug,
        Message = "Artist {ArtistId} not found" )]
    private static partial void LogArtistNotFound( ILogger logger, string artistId );

    /// <summary>Logs an error processing an artist batch.</summary>
    [LoggerMessage(
        EventId = LogEventIds.ArtistBatchError,
        Level = LogLevel.Error,
        Message = "Error processing artist batch of {Count} artists" )]
    private static partial void LogBatchError( ILogger logger, Exception ex, int count );

    #endregion
}
