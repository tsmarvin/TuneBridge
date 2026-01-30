using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace BridgeBeats.Core.Domain.Services.Cards {

    /// <summary>
    /// Background service that periodically cleans up expired anonymous playlists.
    /// </summary>
    public partial class PlaylistCleanupService(
        IPlaylistService playlistService,
        ILogger<PlaylistCleanupService> logger
    ) : BackgroundService {

        private readonly IPlaylistService _playlistService = playlistService;
        private readonly ILogger<PlaylistCleanupService> _logger = logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours( 6 ); // Run every 6 hours

        /// <inheritdoc/>
        protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
            LogPlaylistCleanupStarted( _logger );

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await Task.Delay( _cleanupInterval, stoppingToken );

                    LogRunningCleanup( _logger );
                    await _playlistService.CleanExpiredPlaylistsAsync( );
                    LogCleanupCompleted( _logger );
                } catch (OperationCanceledException) {
                    // Expected when the service is stopping
                    break;
                } catch (Exception ex) {
                    LogCleanupError( _logger, ex );
                }
            }

            LogPlaylistCleanupStopped( _logger );
        }

        #region LoggerMessage Definitions

        /// <summary>
        /// Logs that the playlist cleanup service started.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.PlaylistCleanupStarted,
            Level = LogLevel.Information,
            Message = "Playlist cleanup service started" )]
        private static partial void LogPlaylistCleanupStarted( ILogger logger );

        /// <summary>
        /// Logs that playlist cleanup is running.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.RunningCleanup,
            Level = LogLevel.Information,
            Message = "Running playlist cleanup..." )]
        private static partial void LogRunningCleanup( ILogger logger );

        /// <summary>
        /// Logs that playlist cleanup completed.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.CleanupCompleted,
            Level = LogLevel.Information,
            Message = "Playlist cleanup completed" )]
        private static partial void LogCleanupCompleted( ILogger logger );

        /// <summary>
        /// Logs an error during playlist cleanup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.CleanupError,
            Level = LogLevel.Error,
            Message = "Error during playlist cleanup" )]
        private static partial void LogCleanupError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs that the playlist cleanup service stopped.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.PlaylistCleanupStopped,
            Level = LogLevel.Information,
            Message = "Playlist cleanup service stopped" )]
        private static partial void LogPlaylistCleanupStopped( ILogger logger );

        #endregion LoggerMessage Definitions
    }
}
