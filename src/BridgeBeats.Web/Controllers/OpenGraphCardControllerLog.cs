namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Source-generated <see cref="Microsoft.Extensions.Logging.LoggerMessageAttribute"/> logging methods
/// for <see cref="OpenGraphCardController"/>.
/// </summary>
public partial class OpenGraphCardController {
    /// <summary>
    /// Logs that resolving the ATProto URI from the cache for a card failed with an invalid-operation error.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.OpenGraphCardControllerCacheInvalidOp,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache for card (InvalidOperationException)" )]
    private partial void LogCacheInvalidOp( Exception ex );

    /// <summary>
    /// Logs that resolving the ATProto URI from the cache for a card failed with an argument error.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.OpenGraphCardControllerCacheArgError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache for card (ArgumentException)" )]
    private partial void LogCacheArgError( Exception ex );

    /// <summary>
    /// Logs that resolving the ATProto URI from the cache for a card failed.
    /// </summary>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.OpenGraphCardControllerCacheError,
        Level = LogLevel.Warning,
        Message = "Failed to retrieve ATProto URI from cache for card" )]
    private partial void LogCacheError( Exception ex );
}
