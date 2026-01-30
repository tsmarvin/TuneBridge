namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="PlaylistController"/>.
/// </summary>
public partial class PlaylistController {
    /// <summary>
    /// Logs error creating playlist.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerCreateError,
        Level = LogLevel.Error,
        Message = "Error creating playlist" )]
    private partial void LogCreateError( Exception ex );

    /// <summary>
    /// Logs mismatched card IDs and rkeys warning.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerMismatchedCards,
        Level = LogLevel.Warning,
        Message = "Playlist {PlaylistId} has mismatched card IDs ({CardIdCount}) and rkeys ({RkeyCount})" )]
    private partial void LogMismatchedCards( string playlistId, int cardIdCount, int rkeyCount );

    /// <summary>
    /// Logs regenerated card from rkey.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerCardRegenerated,
        Level = LogLevel.Information,
        Message = "Regenerated card {CardId} from rkey {Rkey} for playlist {PlaylistId}" )]
    private partial void LogCardRegenerated( string cardId, string rkey, string playlistId );

    /// <summary>
    /// Logs unable to load or regenerate card.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerCardLoadFailed,
        Level = LogLevel.Warning,
        Message = "Unable to load or regenerate card {CardId} (rkey: {Rkey}) for playlist {PlaylistId}" )]
    private partial void LogCardLoadFailed( string cardId, string rkey, string playlistId );

    /// <summary>
    /// Logs regenerated card for embed.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerEmbedCardRegenerated,
        Level = LogLevel.Information,
        Message = "Regenerated card {CardId} for playlist embed {PlaylistId}" )]
    private partial void LogEmbedCardRegenerated( string cardId, string playlistId );

    /// <summary>
    /// Logs unable to load or regenerate card for embed.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerEmbedCardLoadFailed,
        Level = LogLevel.Warning,
        Message = "Unable to load or regenerate card {CardId} for playlist embed {PlaylistId}" )]
    private partial void LogEmbedCardLoadFailed( string cardId, string playlistId );
}
