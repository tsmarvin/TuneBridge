namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Source-generated <see cref="Microsoft.Extensions.Logging.LoggerMessageAttribute"/> logging methods
/// for <see cref="PlaylistController"/>.
/// </summary>
public partial class PlaylistController {
    /// <summary>
    /// Logs an unexpected error while creating a playlist.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerCreateError,
        Level = LogLevel.Error,
        Message = "Error creating playlist" )]
    private partial void LogCreateError( Exception ex );

    /// <summary>
    /// Logs that a playlist's card-id and rkey counts do not match.
    /// </summary>
    /// <param name="playlistId">The (sanitized) playlist id.</param>
    /// <param name="cardIdCount">The number of card ids.</param>
    /// <param name="rkeyCount">The number of rkeys.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerMismatchedCards,
        Level = LogLevel.Warning,
        Message = "Playlist {PlaylistId} has mismatched card IDs ({CardIdCount}) and rkeys ({RkeyCount})" )]
    private partial void LogMismatchedCards( string playlistId, int cardIdCount, int rkeyCount );

    /// <summary>
    /// Logs that a card was regenerated from its rkey while rendering a playlist page.
    /// </summary>
    /// <param name="cardId">The (sanitized) regenerated card id.</param>
    /// <param name="rkey">The (sanitized) rkey used to regenerate the card.</param>
    /// <param name="playlistId">The (sanitized) playlist id.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerCardRegenerated,
        Level = LogLevel.Information,
        Message = "Regenerated card {CardId} from rkey {Rkey} for playlist {PlaylistId}" )]
    private partial void LogCardRegenerated( string cardId, string rkey, string playlistId );

    /// <summary>
    /// Logs that a card could not be loaded or regenerated while rendering a playlist page.
    /// </summary>
    /// <param name="cardId">The (sanitized) card id that failed.</param>
    /// <param name="rkey">The (sanitized) rkey that could not be used to regenerate the card.</param>
    /// <param name="playlistId">The (sanitized) playlist id.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerCardLoadFailed,
        Level = LogLevel.Warning,
        Message = "Unable to load or regenerate card {CardId} (rkey: {Rkey}) for playlist {PlaylistId}" )]
    private partial void LogCardLoadFailed( string cardId, string rkey, string playlistId );

    /// <summary>
    /// Logs that a card was regenerated from its rkey while rendering a playlist embed.
    /// </summary>
    /// <param name="cardId">The (sanitized) regenerated card id.</param>
    /// <param name="playlistId">The (sanitized) playlist id.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerEmbedCardRegenerated,
        Level = LogLevel.Information,
        Message = "Regenerated card {CardId} for playlist embed {PlaylistId}" )]
    private partial void LogEmbedCardRegenerated( string cardId, string playlistId );

    /// <summary>
    /// Logs that a card could not be loaded or regenerated while rendering a playlist embed.
    /// </summary>
    /// <param name="cardId">The (sanitized) card id that failed.</param>
    /// <param name="playlistId">The (sanitized) playlist id.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.PlaylistControllerEmbedCardLoadFailed,
        Level = LogLevel.Warning,
        Message = "Unable to load or regenerate card {CardId} for playlist embed {PlaylistId}" )]
    private partial void LogEmbedCardLoadFailed( string cardId, string playlistId );
}
