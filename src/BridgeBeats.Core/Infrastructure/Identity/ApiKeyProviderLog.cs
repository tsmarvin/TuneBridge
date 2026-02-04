using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// LoggerMessage methods for <see cref="ApiKeyProvider"/>.
/// </summary>
public partial class ApiKeyProvider {
    /// <summary>
    /// Logs a database error while validating API key.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.ApiKeyProviderDbError,
        Level = LogLevel.Error,
        Message = "Database error while validating API key" )]
    private partial void LogDbError( Exception ex );

    /// <summary>
    /// Logs an invalid operation while validating API key.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.ApiKeyProviderInvalidOperation,
        Level = LogLevel.Error,
        Message = "Invalid operation while validating API key" )]
    private partial void LogInvalidOperation( Exception ex );
}
