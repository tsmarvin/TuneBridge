namespace BridgeBeats.Web.Controllers;

/// <summary>
/// LoggerMessage methods for <see cref="StatisticsController"/>.
/// </summary>
public partial class StatisticsController {
    /// <summary>
    /// Logs that statistics service is not available.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.StatisticsControllerNotAvailable,
        Level = LogLevel.Warning,
        Message = "Statistics service not available" )]
    private partial void LogNotAvailable( );

    /// <summary>
    /// Logs error retrieving statistics.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.StatisticsControllerRetrieveError,
        Level = LogLevel.Error,
        Message = "Error retrieving statistics" )]
    private partial void LogRetrieveError( Exception ex );

    /// <summary>
    /// Logs error refreshing statistics.
    /// </summary>
    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.StatisticsControllerRefreshError,
        Level = LogLevel.Error,
        Message = "Error refreshing statistics" )]
    private partial void LogRefreshError( Exception ex );
}
