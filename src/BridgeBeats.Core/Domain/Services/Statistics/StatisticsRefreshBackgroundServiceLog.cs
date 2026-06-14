using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Services;

/// <summary>
/// Source-generated <see cref="LoggerMessage"/> definitions for
/// <see cref="StatisticsRefreshBackgroundService"/>.
/// </summary>
public sealed partial class StatisticsRefreshBackgroundService {

    /// <summary>Logs (Information) that the service is starting, with its startup delay and refresh interval.</summary>
    /// <param name="startupDelay">The delay before the first refresh.</param>
    /// <param name="interval">The periodic refresh interval.</param>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshStarting,
        Level = LogLevel.Information,
        Message = "Statistics refresh background service starting. Startup delay: {StartupDelay}, interval: {Interval}" )]
    private partial void LogStarting( TimeSpan startupDelay, TimeSpan interval );

    /// <summary>Logs (Information) that the initial forced refresh completed.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshInitialComplete,
        Level = LogLevel.Information,
        Message = "Initial statistics refresh completed" )]
    private partial void LogInitialRefreshComplete( );

    /// <summary>Logs (Debug) that a refresh was triggered by the periodic timer.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshPeriodicTriggered,
        Level = LogLevel.Debug,
        Message = "Statistics refresh triggered by periodic timer" )]
    private partial void LogPeriodicRefreshTriggered( );

    /// <summary>Logs (Debug) that a refresh was triggered manually through the trigger channel.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshManualTriggered,
        Level = LogLevel.Debug,
        Message = "Statistics refresh triggered manually" )]
    private partial void LogManualRefreshTriggered( );

    /// <summary>Logs (Error) that a refresh attempt threw; the loop backs off and continues.</summary>
    /// <param name="ex">The exception raised during the refresh.</param>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshError,
        Level = LogLevel.Error,
        Message = "Error occurred during statistics refresh" )]
    private partial void LogRefreshError( Exception ex );

    /// <summary>Logs (Information) that the refresh loop has stopped.</summary>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshStopped,
        Level = LogLevel.Information,
        Message = "Statistics refresh background service stopped" )]
    private partial void LogStopped( );

    /// <summary>
    /// Logs (Warning) that the manual-trigger channel completed unexpectedly, which ends the loop.
    /// </summary>
    /// <param name="exception">The base exception that faulted the channel wait, if any.</param>
    [LoggerMessage(
        EventId = LogEventIds.BackgroundServices.StatisticsRefreshChannelCompleted,
        Level = LogLevel.Warning,
        Message = "Statistics refresh trigger channel completed unexpectedly; stopping background service loop" )]
    private partial void LogChannelCompleted( Exception? exception );
}
