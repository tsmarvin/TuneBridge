using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Source-generated logging methods for <see cref="ApiKeyProvider"/>.
/// </summary>
public partial class ApiKeyProvider {
    /// <summary>
    /// Logs a database error raised while validating an API key.
    /// </summary>
    /// <param name="ex">The database exception that occurred during lookup.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.ApiKeyProviderDbError,
        Level = LogLevel.Error,
        Message = "Database error while validating API key" )]
    private partial void LogDbError( Exception ex );

    /// <summary>
    /// Logs an invalid-operation error raised while validating an API key.
    /// </summary>
    /// <param name="ex">The invalid-operation exception that occurred during lookup.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.ApiKeyProviderInvalidOperation,
        Level = LogLevel.Error,
        Message = "Invalid operation while validating API key" )]
    private partial void LogInvalidOperation( Exception ex );
}
