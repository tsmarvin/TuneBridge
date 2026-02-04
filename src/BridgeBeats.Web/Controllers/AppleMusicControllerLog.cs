namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="AppleMusicController"/>.
/// </summary>
public partial class AppleMusicController {
    /// <summary>
    /// Logs failure to store Apple Music token.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerStoreTokenFailed,
        Level = LogLevel.Error,
        Message = "Failed to store Apple Music token for user {UserId}" )]
    private partial void LogStoreTokenFailed( string userId );

    /// <summary>
    /// Logs successful Apple Music token storage.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerTokenStored,
        Level = LogLevel.Information,
        Message = "Apple Music token stored for user {UserId}" )]
    private partial void LogTokenStored( string userId );

    /// <summary>
    /// Logs failure to retrieve playlists.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerGetPlaylistsFailed,
        Level = LogLevel.Error,
        Message = "Failed to retrieve playlists for user {UserId}: {StatusCode}" )]
    private partial void LogGetPlaylistsFailed( string userId, int statusCode );

    /// <summary>
    /// Logs error retrieving playlists.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerGetPlaylistsError,
        Level = LogLevel.Error,
        Message = "Error retrieving playlists for user {UserId}" )]
    private partial void LogGetPlaylistsError( Exception ex, string userId );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to invalid operation.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerCacheInvalidOp,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache due to invalid operation, continuing without it" )]
    private partial void LogCacheInvalidOp( Exception ex );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to argument error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerCacheArgError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache due to argument error, continuing without it" )]
    private partial void LogCacheArgError( Exception ex );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to general error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerCacheError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache, continuing without it" )]
    private partial void LogCacheError( Exception ex );

    /// <summary>
    /// Logs failure to retrieve playlist tracks.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerGetTracksFailed,
        Level = LogLevel.Error,
        Message = "Failed to retrieve playlist tracks for user {UserId}: {StatusCode}" )]
    private partial void LogGetTracksFailed( string userId, int statusCode );

    /// <summary>
    /// Logs playlist over 1000 tracks warning.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerPlaylistTooLarge,
        Level = LogLevel.Warning,
        Message = "Playlist {PlaylistId} exceeds 1000 tracks, processing limited to first 1000 tracks" )]
    private partial void LogPlaylistTooLarge( string playlistId );

    /// <summary>
    /// Logs error processing playlist.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerProcessPlaylistError,
        Level = LogLevel.Error,
        Message = "Error processing playlist for user {UserId}" )]
    private partial void LogProcessPlaylistError( Exception ex, string userId );

    /// <summary>
    /// Logs error processing song in stream.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.AppleMusicControllerProcessSongError,
        Level = LogLevel.Error,
        Message = "Error processing song {SongId}" )]
    private partial void LogProcessSongError( Exception ex, string songId );
}
