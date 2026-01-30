using BridgeBeats.Web.Logging;

namespace BridgeBeats.Web.Configuration;

/// <summary>
/// LoggerMessage methods for <see cref="StartupExtensions"/>.
/// </summary>
internal static partial class StartupExtensionsLog {
    /// <summary>
    /// Logs Redis not configured warning.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Configuration.StartupExtensionsRedisNotConfigured,
        Level = LogLevel.Warning,
        Message = "BridgeBeats: Redis not configured - caching will be unavailable" )]
    internal static partial void LogRedisNotConfigured( ILogger logger );

    /// <summary>
    /// Logs Redis cache connection established.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Configuration.StartupExtensionsRedisConnected,
        Level = LogLevel.Information,
        Message = "BridgeBeats: Redis cache connection established successfully" )]
    internal static partial void LogRedisConnected( ILogger logger );

    /// <summary>
    /// Logs Redis cache initialization failure.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Configuration.StartupExtensionsRedisFailed,
        Level = LogLevel.Error,
        Message = "Failed to initialize Redis cache connection" )]
    internal static partial void LogRedisFailed( ILogger logger, Exception ex );
}
