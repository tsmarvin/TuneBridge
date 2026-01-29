using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.Hosting;

namespace BridgeBeats.Core.Domain.Services.Cards {

    /// <summary>
    /// Background service that periodically cleans up expired anonymous playlists.
    /// </summary>
    public class PlaylistCleanupService(
        IPlaylistService playlistService,
        ILogger<PlaylistCleanupService> logger
    ) : BackgroundService {

        private readonly IPlaylistService _playlistService = playlistService;
        private readonly ILogger<PlaylistCleanupService> _logger = logger;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours( 6 ); // Run every 6 hours

        /// <inheritdoc/>
        protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
            _logger.LogInformation( "Playlist cleanup service started" );

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    await Task.Delay( _cleanupInterval, stoppingToken );

                    _logger.LogInformation( "Running playlist cleanup..." );
                    await _playlistService.CleanExpiredPlaylistsAsync( );
                    _logger.LogInformation( "Playlist cleanup completed" );
                } catch (OperationCanceledException) {
                    // Expected when the service is stopping
                    break;
                } catch (Exception ex) {
                    _logger.LogError( ex, "Error during playlist cleanup" );
                }
            }

            _logger.LogInformation( "Playlist cleanup service stopped" );
        }
    }
}
