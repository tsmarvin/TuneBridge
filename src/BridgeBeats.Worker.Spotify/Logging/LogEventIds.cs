namespace BridgeBeats.Worker.Spotify.Logging;

/// <summary>
/// EventIds for Spotify worker (5250-5499).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with Spotify worker-specific EventIds.
/// </summary>
public static class LogEventIds {
    // SpotifyArtistGenreService (5250-5299)
    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogServiceStarting"/>.</summary>
    public const int ArtistGenreServiceStarting = 5250;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogStartingScheduledRefresh"/>.</summary>
    public const int StartingScheduledRefresh = 5251;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogCompletedScheduledRefresh"/>.</summary>
    public const int CompletedScheduledRefresh = 5252;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogProcessorLoopError"/>.</summary>
    public const int ArtistProcessorLoopError = 5253;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogServiceStopping"/>.</summary>
    public const int ArtistGenreServiceStopping = 5254;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogQueueLength"/>.</summary>
    public const int ArtistQueueLength = 5255;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogQueueEmpty"/>.</summary>
    public const int ArtistQueueEmpty = 5256;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogBatchProcessed"/>.</summary>
    public const int ArtistBatchProcessed = 5257;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogRefreshComplete"/>.</summary>
    public const int ArtistRefreshComplete = 5258;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogArtistNotFound"/>.</summary>
    public const int ArtistNotFound = 5259;

    /// <summary>EventId for <see cref="SpotifyArtistGenreService.LogBatchError"/>.</summary>
    public const int ArtistBatchError = 5260;

    // SpotifyBatchQueueHelper (5300-5349)
    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogConsumerGroupCreated"/>.</summary>
    public const int ConsumerGroupCreated = 5300;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogConsumerGroupExists"/>.</summary>
    public const int ConsumerGroupExists = 5301;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogEnqueuedToBulkStream"/>.</summary>
    public const int EnqueuedToBulkStream = 5302;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogCollectionWarning"/>.</summary>
    public const int CollectionWarning = 5303;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogDeserializationError"/>.</summary>
    public const int DeserializationError = 5304;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogStreamReadWarning"/>.</summary>
    public const int StreamReadWarning = 5305;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogMessageAcknowledged"/>.</summary>
    public const int MessageAcknowledged = 5306;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogMessageNotFound"/>.</summary>
    public const int MessageNotFound = 5307;

    /// <summary>EventId for <see cref="SpotifyBatchQueueHelper.LogMessageRequeued"/>.</summary>
    public const int MessageRequeued = 5308;

    // SpotifyBulkProcessorService (5350-5399)
    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogServiceStarting"/>.</summary>
    public const int BulkProcessorStarting = 5350;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogProcessorLoopError"/>.</summary>
    public const int BulkProcessorLoopError = 5351;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogServiceStopping"/>.</summary>
    public const int BulkProcessorStopping = 5352;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogRateLimited"/>.</summary>
    public const int BulkRateLimited = 5353;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogProcessingBulkTracks"/>.</summary>
    public const int ProcessingBulkTracks = 5354;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogNoTrackLookups"/>.</summary>
    public const int NoTrackLookups = 5355;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogProcessingTrackCount"/>.</summary>
    public const int ProcessingTrackCount = 5356;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogTrackLookupsSuccess"/>.</summary>
    public const int TrackLookupsSuccess = 5357;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogTrackLookupsError"/>.</summary>
    public const int TrackLookupsError = 5358;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogProcessingBulkAlbums"/>.</summary>
    public const int ProcessingBulkAlbums = 5359;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogNoAlbumLookups"/>.</summary>
    public const int NoAlbumLookups = 5360;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogProcessingAlbumCount"/>.</summary>
    public const int ProcessingAlbumCount = 5361;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogAlbumLookupsSuccess"/>.</summary>
    public const int AlbumLookupsSuccess = 5362;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogAlbumLookupsError"/>.</summary>
    public const int AlbumLookupsError = 5363;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogBulkResultProcessed"/>.</summary>
    public const int BulkResultProcessed = 5364;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogBulkResultError"/>.</summary>
    public const int BulkResultError = 5365;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogRateLimitEncountered"/>.</summary>
    public const int RateLimitEncountered = 5366;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogRequeueError"/>.</summary>
    public const int RequeueError = 5367;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogSagaComplete"/>.</summary>
    public const int SagaComplete = 5368;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogSagaCompletionCheckError"/>.</summary>
    public const int SagaCompletionCheckError = 5369;

    /// <summary>EventId for <see cref="SpotifyBulkProcessorService.LogPublishCompletionError"/>.</summary>
    public const int PublishCompletionError = 5370;
}
