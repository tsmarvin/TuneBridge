using BridgeBeats.Worker.CacheBootstrap.Logging;

namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// LoggerMessage methods for CacheBootstrap Program startup.
/// </summary>
internal static partial class ProgramLog {
    /// <summary>
    /// Logs Redis connection details.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisConnectionInfo,
        Level = LogLevel.Information,
        Message = "Redis connection: {Configuration}, IsConnected: {IsConnected}, Database: {Database}" )]
    internal static partial void LogRedisConnectionInfo(
        ILogger logger,
        string? configuration,
        bool isConnected,
        int database );

    /// <summary>
    /// Logs Redis write test result.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisWriteTest,
        Level = LogLevel.Information,
        Message = "Redis write test - SetResult: {SetResult}, ReadBack: {ReadBack}" )]
    internal static partial void LogRedisWriteTest(
        ILogger logger,
        bool setResult,
        string? readBack );

    /// <summary>
    /// Logs Redis write verification failure.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisWriteVerificationFailed,
        Level = LogLevel.Error,
        Message = "Redis write verification failed! SetResult: {SetResult}, ReadBack: {ReadBack}" )]
    internal static partial void LogRedisWriteVerificationFailed(
        ILogger logger,
        bool setResult,
        string? readBack );

    /// <summary>
    /// Logs Redis server key count.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.RedisKeyCount,
        Level = LogLevel.Information,
        Message = "Redis server {Endpoint} has {KeyCount} keys" )]
    internal static partial void LogRedisKeyCount(
        ILogger logger,
        string endpoint,
        long keyCount );
}
