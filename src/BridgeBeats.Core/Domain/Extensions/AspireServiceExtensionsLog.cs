using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Extensions;

/// <summary>
/// LoggerMessage methods for <see cref="AspireServiceExtensions"/>.
/// </summary>
internal static partial class AspireServiceExtensionsLog {
    /// <summary>
    /// Logs HTTP retry with Retry-After header.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Other.AspireServiceExtensionsRetryWithHeader,
        Level = LogLevel.Warning,
        Message = "HTTP request failed (Attempt {AttemptNumber}/{MaxAttempts}). Retrying after {RetryAfterSeconds} seconds due to Retry-After header. Uri: {Uri}" )]
    internal static partial void LogRetryWithRetryAfterHeader(
        ILogger logger,
        int attemptNumber,
        int maxAttempts,
        double retryAfterSeconds,
        Uri? uri );

    /// <summary>
    /// Logs HTTP retry with exponential backoff.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Other.AspireServiceExtensionsRetryWithBackoff,
        Level = LogLevel.Warning,
        Message = "HTTP request failed (Attempt {AttemptNumber}/{MaxAttempts}). Retrying with exponential backoff. Uri: {Uri}" )]
    internal static partial void LogRetryWithExponentialBackoff(
        ILogger logger,
        int attemptNumber,
        int maxAttempts,
        Uri? uri );
}
