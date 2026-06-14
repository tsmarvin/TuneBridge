namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Source-generated <see cref="Microsoft.Extensions.Logging.LoggerMessageAttribute"/> logging methods
/// for <see cref="PlaylistsController"/>.
/// </summary>
public partial class PlaylistsController {
    /// <summary>
    /// Logs an error while loading a user's playlists.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="userId">The id of the user whose playlists were being loaded.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistsControllerLoadError,
        Level = LogLevel.Error,
        Message = "Error loading user playlists for user {UserId}" )]
    private partial void LogLoadError( Exception ex, string userId );

    /// <summary>
    /// Logs an error while deleting a user's playlist.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    /// <param name="playlistId">The (sanitized) id of the playlist being deleted.</param>
    /// <param name="userId">The id of the user who owns the playlist.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistsControllerDeleteError,
        Level = LogLevel.Error,
        Message = "Error deleting playlist {PlaylistId} for user {UserId}" )]
    private partial void LogDeleteError( Exception ex, string playlistId, string userId );
}
