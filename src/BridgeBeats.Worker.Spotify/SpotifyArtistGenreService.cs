using BridgeBeats.Contracts.Constants;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Providers.Spotify;
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
public sealed class SpotifyArtistGenreService : BackgroundService {

    private readonly IConnectionMultiplexer _redis;
    private readonly IGenreCacheService _genreCache;
    private readonly SpotifyLookupService _lookupService;
    private readonly ILogger<SpotifyArtistGenreService> _logger;

    private static readonly TimeSpan s_checkInterval = TimeSpan.FromMinutes( 5 );
    private static readonly TimeSpan s_scheduleInterval = TimeSpan.FromDays( 7 );
    private static readonly TimeSpan s_errorDelay = TimeSpan.FromMinutes( 1 );

    /// <summary>
    /// Initializes a new instance of the <see cref="SpotifyArtistGenreService"/> class.
    /// </summary>
    public SpotifyArtistGenreService(
        IConnectionMultiplexer redis,
        IGenreCacheService genreCache,
        SpotifyLookupService lookupService,
        ILogger<SpotifyArtistGenreService> logger
    ) {
        _redis = redis ?? throw new ArgumentNullException( nameof( redis ) );
        _genreCache = genreCache ?? throw new ArgumentNullException( nameof( genreCache ) );
        _lookupService = lookupService ?? throw new ArgumentNullException( nameof( lookupService ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        _logger.LogInformation( "Spotify artist genre service starting" );

        // Wait a bit before starting to allow other services to initialize
        await Task.Delay( TimeSpan.FromSeconds( 30 ), stoppingToken );

        DateTimeOffset lastFullRun = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                // Check if it's time for a full scheduled run
                bool shouldRunScheduled = DateTimeOffset.UtcNow - lastFullRun >= s_scheduleInterval;

                if (shouldRunScheduled) {
                    _logger.LogInformation( "Starting scheduled artist genre refresh" );
                    await ProcessAllQueuedArtistsAsync( stoppingToken );
                    lastFullRun = DateTimeOffset.UtcNow;
                    _logger.LogInformation( "Completed scheduled artist genre refresh" );
                }

                // Wait before checking again
                await Task.Delay( s_checkInterval, stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error in artist genre processor loop" );
                await Task.Delay( s_errorDelay, stoppingToken );
            }
        }

        _logger.LogInformation( "Spotify artist genre service stopping" );
    }

    /// <summary>
    /// Processes all queued artists in batches until the queue is empty.
    /// </summary>
    private async Task ProcessAllQueuedArtistsAsync( CancellationToken ct ) {
        long queueLength = await _genreCache.GetArtistRefreshQueueLengthAsync( SupportedProviders.Spotify, ct );
        _logger.LogInformation( "Artist refresh queue has {QueueLength} artists pending", queueLength );

        int totalProcessed = 0;
        int batchCount = 0;

        while (!ct.IsCancellationRequested) {
            IReadOnlyList<string> artistIds = await _genreCache.DequeueArtistsForRefreshAsync(
                SupportedProviders.Spotify,
                SpotifyConstants.MaxArtistsPerBatchLookup,
                ct
            );

            if (artistIds.Count == 0) {
                _logger.LogDebug( "Artist refresh queue is empty" );
                break;
            }

            batchCount++;
            int processed = await ProcessArtistBatchAsync( artistIds, ct );
            totalProcessed += processed;

            _logger.LogDebug(
                "Batch {BatchNumber}: Processed {Processed}/{Total} artists",
                batchCount,
                processed,
                artistIds.Count
            );

            // Small delay between batches to be nice to Spotify's rate limits
            await Task.Delay( TimeSpan.FromMilliseconds( 100 ), ct );
        }

        _logger.LogInformation(
            "Artist genre refresh complete: {TotalProcessed} artists in {BatchCount} batches",
            totalProcessed,
            batchCount
        );
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
                    _logger.LogDebug( "Artist {ArtistId} not found", artistId );
                    continue;
                }

                // Cache the artist genres (empty list is valid - means artist has no genres)
                await _genreCache.SetArtistGenresAsync( SupportedProviders.Spotify, artistId, genres, ct );
                successCount++;
            }

            return successCount;
        } catch (Exception ex) {
            _logger.LogError( ex, "Error processing artist batch of {Count} artists", artistIds.Count );
            return 0;
        }
    }
}
