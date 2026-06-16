using BridgeBeats.Web.Logging;

namespace BridgeBeats.Web.Configuration;

/// <summary>
/// Source-generated <see cref="LoggerMessage"/> definitions for Redis-related startup events.
/// </summary>
internal static partial class StartupExtensionsLog {
    /// <summary>
    /// Logs a warning that Redis is not configured and caching will be unavailable.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    [LoggerMessage(
        EventId = LogEventIds.Configuration.StartupExtensionsRedisNotConfigured,
        Level = LogLevel.Warning,
        Message = "BridgeBeats: Redis not configured - caching will be unavailable" )]
    internal static partial void LogRedisNotConfigured( ILogger logger );

    /// <summary>
    /// Logs that the Redis cache connection was established successfully.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    [LoggerMessage(
        EventId = LogEventIds.Configuration.StartupExtensionsRedisConnected,
        Level = LogLevel.Information,
        Message = "BridgeBeats: Redis cache connection established successfully" )]
    internal static partial void LogRedisConnected( ILogger logger );

    /// <summary>
    /// Logs an error when initializing the Redis cache connection fails.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="ex">The exception that caused the failure.</param>
    [LoggerMessage(
        EventId = LogEventIds.Configuration.StartupExtensionsRedisFailed,
        Level = LogLevel.Error,
        Message = "Failed to initialize Redis cache connection" )]
    internal static partial void LogRedisFailed( ILogger logger, Exception ex );
}
