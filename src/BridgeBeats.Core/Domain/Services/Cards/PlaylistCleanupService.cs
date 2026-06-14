using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace BridgeBeats.Core.Domain.Services.Cards {

    /// <summary>
    /// Background maintenance service that periodically purges expired anonymous playlists by
    /// calling <see cref="IPlaylistService.CleanExpiredPlaylistsAsync"/> on a fixed interval.
    /// </summary>
    /// <param name="playlistService">The playlist service whose expired entries are cleaned.</param>
    /// <param name="logger">The logger for service lifecycle and cleanup-cycle messages.</param>
    public partial class PlaylistCleanupService(
        IPlaylistService playlistService,
        ILogger<PlaylistCleanupService> logger
    ) : BackgroundService {

        /// <summary>The playlist service whose expired entries are removed each cycle.</summary>
        private readonly IPlaylistService _playlistService = playlistService;
        /// <summary>The logger for lifecycle and per-cycle cleanup messages.</summary>
        private readonly ILogger<PlaylistCleanupService> _logger = logger;
        /// <summary>The delay between cleanup cycles. Fixed at 6 hours.</summary>
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours( 6 ); // Run every 6 hours

        /// <summary>
        /// Runs the cleanup loop until the host stops. Each iteration waits one
        /// <see cref="_cleanupInterval"/>, then removes expired playlists. Cancellation ends the
        /// loop cleanly; other exceptions are logged and the loop continues.
        /// </summary>
        /// <param name="stoppingToken">Signals that the host is shutting down.</param>
        /// <returns>A task that completes when the service stops.</returns>
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

        /// <summary>Logs (Information) that the cleanup service has started its loop.</summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.PlaylistCleanupStarted,
            Level = LogLevel.Information,
            Message = "Playlist cleanup service started" )]
        private static partial void LogPlaylistCleanupStarted( ILogger logger );

        /// <summary>Logs (Information) that a cleanup cycle is starting.</summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.RunningCleanup,
            Level = LogLevel.Information,
            Message = "Running playlist cleanup..." )]
        private static partial void LogRunningCleanup( ILogger logger );

        /// <summary>Logs (Information) that a cleanup cycle finished successfully.</summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.CleanupCompleted,
            Level = LogLevel.Information,
            Message = "Playlist cleanup completed" )]
        private static partial void LogCleanupCompleted( ILogger logger );

        /// <summary>Logs (Error) that a cleanup cycle threw. The loop continues after this.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="ex">The exception raised during cleanup.</param>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.CleanupError,
            Level = LogLevel.Error,
            Message = "Error during playlist cleanup" )]
        private static partial void LogCleanupError( ILogger logger, Exception ex );

        /// <summary>Logs (Information) that the cleanup service loop has stopped.</summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = LogEventIds.Services.Cards.PlaylistCleanupStopped,
            Level = LogLevel.Information,
            Message = "Playlist cleanup service stopped" )]
        private static partial void LogPlaylistCleanupStopped( ILogger logger );

        #endregion LoggerMessage Definitions
    }
}
