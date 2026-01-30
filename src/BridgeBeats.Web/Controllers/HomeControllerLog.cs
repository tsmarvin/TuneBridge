namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="HomeController"/>.
/// </summary>
public partial class HomeController {
    /// <summary>
    /// Logs ATProto URI retrieval failure due to invalid operation.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.HomeControllerCacheInvalidOp,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache due to invalid operation, continuing without it" )]
    private partial void LogCacheInvalidOp( Exception ex );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to argument error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.HomeControllerCacheArgError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache due to argument error, continuing without it" )]
    private partial void LogCacheArgError( Exception ex );

    /// <summary>
    /// Logs ATProto URI retrieval failure due to general error.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.HomeControllerCacheError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache, continuing without it" )]
    private partial void LogCacheError( Exception ex );

    /// <summary>
    /// Logs error processing individual result in stream.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.HomeControllerStreamResultError,
        Level = LogLevel.Error,
        Message = "Error processing individual result for URI: {Uri}" )]
    private partial void LogStreamResultError( Exception ex, string uri );

    /// <summary>
    /// Logs error during entire lookup stream.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.HomeControllerStreamError,
        Level = LogLevel.Error,
        Message = "Error during lookup stream for URI: {Uri}" )]
    private partial void LogStreamError( Exception ex, string uri );
}
