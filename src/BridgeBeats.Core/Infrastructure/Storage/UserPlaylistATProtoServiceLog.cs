using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// LoggerMessage methods for <see cref="UserPlaylistATProtoService"/>.
/// </summary>
public partial class UserPlaylistATProtoService {
    /// <summary>
    /// Logs that a playlist was created successfully.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceCreated,
        Level = LogLevel.Information,
        Message = "Successfully created playlist '{Title}' for user {UserDid}: {Uri}" )]
    private partial void LogPlaylistCreated( string title, string userDid, string uri );

    /// <summary>
    /// Logs an error creating a playlist.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceCreateFailed,
        Level = LogLevel.Error,
        Message = "Failed to create playlist for user {UserDid}" )]
    private partial void LogCreateFailed( Exception ex, string userDid );

    /// <summary>
    /// Logs a warning when getting playlist from PDS failed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceGetFailed,
        Level = LogLevel.Warning,
        Message = "Failed to get playlist from PDS: {Error}" )]
    private partial void LogGetFailed( string error );

    /// <summary>
    /// Logs an error retrieving playlist from PDS.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceRetrieveError,
        Level = LogLevel.Error,
        Message = "Failed to retrieve playlist from PDS: {Uri}" )]
    private partial void LogRetrieveError( Exception ex, string uri );

    /// <summary>
    /// Logs that a playlist was updated successfully.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceUpdated,
        Level = LogLevel.Information,
        Message = "Successfully updated playlist '{Title}' for user {UserDid}: {Uri}" )]
    private partial void LogPlaylistUpdated( string title, string userDid, string uri );

    /// <summary>
    /// Logs an error updating a playlist.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceUpdateFailed,
        Level = LogLevel.Error,
        Message = "Failed to update playlist for user {UserDid}" )]
    private partial void LogUpdateFailed( Exception ex, string userDid );

    /// <summary>
    /// Logs a warning when deleting playlist failed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceDeleteWarning,
        Level = LogLevel.Warning,
        Message = "Failed to delete playlist for user {UserDid}: {Error}" )]
    private partial void LogDeleteWarning( string userDid, string error );

    /// <summary>
    /// Logs that a playlist was deleted successfully.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceDeleted,
        Level = LogLevel.Information,
        Message = "Successfully deleted playlist {Rkey} for user {UserDid}" )]
    private partial void LogPlaylistDeleted( string rkey, string userDid );

    /// <summary>
    /// Logs an error deleting a playlist.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceDeleteError,
        Level = LogLevel.Error,
        Message = "Failed to delete playlist {Rkey} for user {UserDid}" )]
    private partial void LogDeleteError( Exception ex, string rkey, string userDid );

    /// <summary>
    /// Logs an error listing playlists.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceListFailed,
        Level = LogLevel.Error,
        Message = "Failed to list playlists for user {UserDid}: {Error}" )]
    private partial void LogListFailed( string userDid, string error );

    /// <summary>
    /// Logs that ATProto tokens are being refreshed.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceRefreshingTokens,
        Level = LogLevel.Debug,
        Message = "ATProto tokens expired for user {UserDid}, attempting refresh" )]
    private partial void LogRefreshingTokens( string userDid );

    /// <summary>
    /// Logs that ATProto tokens were refreshed successfully.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.UserPlaylistATProtoServiceTokensRefreshed,
        Level = LogLevel.Information,
        Message = "Successfully refreshed ATProto tokens for user {UserDid}" )]
    private partial void LogTokensRefreshed( string userDid );
}
