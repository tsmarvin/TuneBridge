namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="PlaylistsController"/>.
/// </summary>
public partial class PlaylistsController {
    /// <summary>
    /// Logs error loading user playlists.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistsControllerLoadError,
        Level = LogLevel.Error,
        Message = "Error loading user playlists for user {UserId}" )]
    private partial void LogLoadError( Exception ex, string userId );

    /// <summary>
    /// Logs error deleting playlist.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistsControllerDeleteError,
        Level = LogLevel.Error,
        Message = "Error deleting playlist {PlaylistId} for user {UserId}" )]
    private partial void LogDeleteError( Exception ex, string playlistId, string userId );
}
