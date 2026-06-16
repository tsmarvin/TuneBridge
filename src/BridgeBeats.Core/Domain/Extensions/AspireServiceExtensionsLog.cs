using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Extensions;

/// <summary>
/// Source-generated <c>LoggerMessage</c> partials for the HTTP resilience retry logs emitted by the
/// standard resilience pipeline configured in <see cref="AspireServiceExtensions"/>. Each method
/// records one retry attempt at warning level.
/// </summary>
internal static partial class AspireServiceExtensionsLog {
    /// <summary>
    /// Logs a retry that is honoring a server <c>Retry-After</c> header, including the delay the
    /// server asked for.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="attemptNumber">The current retry attempt number.</param>
    /// <param name="maxAttempts">The configured maximum number of retry attempts.</param>
    /// <param name="retryAfterSeconds">The wait imposed by the <c>Retry-After</c> header, in seconds.</param>
    /// <param name="uri">The request URI that failed, when available.</param>
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
    /// Logs a retry that is backing off on the pipeline's own exponential schedule because no
    /// <c>Retry-After</c> header was present.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="attemptNumber">The current retry attempt number.</param>
    /// <param name="maxAttempts">The configured maximum number of retry attempts.</param>
    /// <param name="uri">The request URI that failed, when available.</param>
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
