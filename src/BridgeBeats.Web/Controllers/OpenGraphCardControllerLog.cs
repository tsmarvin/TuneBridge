namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="OpenGraphCardController"/>.
/// </summary>
public partial class OpenGraphCardController {
    /// <summary>
    /// Logs ATProto URI retrieval failure due to invalid operation.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.OpenGraphCardControllerCacheInvalidOp,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache for card (InvalidOperationException)" )]
    private partial void LogCacheInvalidOp( Exception ex );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to argument error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.OpenGraphCardControllerCacheArgError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache for card (ArgumentException)" )]
    private partial void LogCacheArgError( Exception ex );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to general error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.OpenGraphCardControllerCacheError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache for card" )]
    private partial void LogCacheError( Exception ex );
}
