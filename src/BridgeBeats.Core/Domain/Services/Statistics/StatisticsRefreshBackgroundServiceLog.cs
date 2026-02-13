using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Services;

/// <summary>
/// LoggerMessage methods for <see cref="StatisticsRefreshBackgroundService"/>.
/// </summary>
public sealed partial class StatisticsRefreshBackgroundService {

    /// <summary>
    /// Logs when the statistics refresh background service starts.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshStarting,
        Level = LogLevel.Information,
        Message = "Statistics refresh background service starting. Startup delay: {StartupDelay}, interval: {Interval}" )]
    private partial void LogStarting( TimeSpan startupDelay, TimeSpan interval );

    /// <summary>
    /// Logs when the initial refresh completes.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshInitialComplete,
        Level = LogLevel.Information,
        Message = "Initial statistics refresh completed" )]
    private partial void LogInitialRefreshComplete( );

    /// <summary>
    /// Logs when the periodic timer triggered a refresh.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshPeriodicTriggered,
        Level = LogLevel.Debug,
        Message = "Statistics refresh triggered by periodic timer" )]
    private partial void LogPeriodicRefreshTriggered( );

    /// <summary>
    /// Logs when a manual trigger triggered a refresh.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshManualTriggered,
        Level = LogLevel.Debug,
        Message = "Statistics refresh triggered manually" )]
    private partial void LogManualRefreshTriggered( );

    /// <summary>
    /// Logs refresh errors while keeping the background loop alive.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshError,
        Level = LogLevel.Error,
        Message = "Error occurred during statistics refresh" )]
    private partial void LogRefreshError( Exception ex );

    /// <summary>
    /// Logs when the statistics refresh background service stops.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshStopped,
        Level = LogLevel.Information,
        Message = "Statistics refresh background service stopped" )]
    private partial void LogStopped( );
}
