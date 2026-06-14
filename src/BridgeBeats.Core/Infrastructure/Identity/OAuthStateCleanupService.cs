using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Background service that periodically removes expired ATProto OAuth state rows.
/// </summary>
/// <remarks>
/// Runs an initial sweep at startup and then every six hours. Each pass resolves
/// <see cref="IATProtoOAuthService"/> in a fresh DI scope and calls its cleanup method. Bounding the
/// lifetime of expired state rows limits how long pending-authorization secrets (the encrypted code
/// verifier and DPoP key) remain in the database. Transient failures are logged and swallowed so the
/// loop survives; cancellation ends the loop cleanly.
/// </remarks>
/// <param name="scopeFactory">The factory used to create a DI scope per cleanup pass.</param>
/// <param name="logger">The logger for lifecycle and error messages.</param>
public partial class OAuthStateCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<OAuthStateCleanupService> logger
) : BackgroundService {

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<OAuthStateCleanupService> _logger = logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours( 6 ); // Run every 6 hours

    /// <summary>
    /// Runs the cleanup loop until cancellation: an initial sweep, then a sweep every six hours.
    /// </summary>
    /// <param name="stoppingToken">Signals that the service should stop.</param>
    /// <returns>A task that completes when the service stops.</returns>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogServiceStarted( _logger );

        // Perform initial cleanup immediately
        try {
            LogRunningInitialCleanup( _logger );
            int removedCount = await PerformCleanupAsync( stoppingToken );
            LogInitialCleanupCompleted( _logger, removedCount );
        } catch (Exception ex) {
            LogInitialCleanupError( _logger, ex );
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay( _cleanupInterval, stoppingToken );

                LogRunningCleanup( _logger );
                int removedCount = await PerformCleanupAsync( stoppingToken );
                LogCleanupCompleted( _logger, removedCount );
            } catch (OperationCanceledException) {
                // Expected when the service is stopping
                break;
            } catch (Exception ex) {
                LogCleanupError( _logger, ex );
            }
        }

        LogServiceStopped( _logger );
    }

    /// <summary>
    /// Performs a single cleanup pass within a fresh DI scope.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the cleanup operation.</param>
    /// <returns>The number of expired state rows removed during this pass.</returns>
    private async Task<int> PerformCleanupAsync( CancellationToken cancellationToken ) {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope( );
        IATProtoOAuthService oauthService = scope.ServiceProvider.GetRequiredService<IATProtoOAuthService>( );
        return await oauthService.CleanupExpiredStatesAsync( cancellationToken );
    }

    #region LoggerMessage Methods

    /// <summary>Logs that the cleanup service has started.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceStarted,
        Level = LogLevel.Information,
        Message = "OAuth state cleanup service started" )]
    internal static partial void LogServiceStarted( ILogger logger );

    /// <summary>Logs that the initial startup cleanup pass is beginning.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceInitialCleanup,
        Level = LogLevel.Information,
        Message = "Running initial OAuth state cleanup..." )]
    internal static partial void LogRunningInitialCleanup( ILogger logger );

    /// <summary>Logs completion of the initial cleanup pass and the number of rows removed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of expired state rows removed.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceInitialCleanupCompleted,
        Level = LogLevel.Information,
        Message = "Initial OAuth state cleanup completed. Removed {Count} expired entries." )]
    internal static partial void LogInitialCleanupCompleted( ILogger logger, int count );

    /// <summary>Logs an error raised during the initial cleanup pass.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceInitialCleanupError,
        Level = LogLevel.Error,
        Message = "Error during initial OAuth state cleanup" )]
    internal static partial void LogInitialCleanupError( ILogger logger, Exception ex );

    /// <summary>Logs that a periodic cleanup pass is beginning.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceRunningCleanup,
        Level = LogLevel.Information,
        Message = "Running OAuth state cleanup..." )]
    internal static partial void LogRunningCleanup( ILogger logger );

    /// <summary>Logs completion of a periodic cleanup pass and the number of rows removed.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="count">The number of expired state rows removed.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceCleanupCompleted,
        Level = LogLevel.Information,
        Message = "OAuth state cleanup completed. Removed {Count} expired entries." )]
    internal static partial void LogCleanupCompleted( ILogger logger, int count );

    /// <summary>Logs an error raised during a periodic cleanup pass.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that occurred.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceCleanupError,
        Level = LogLevel.Error,
        Message = "Error during OAuth state cleanup" )]
    internal static partial void LogCleanupError( ILogger logger, Exception ex );

    /// <summary>Logs that the cleanup service has stopped.</summary>
    /// <param name="logger">The logger to write to.</param>
    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceStopped,
        Level = LogLevel.Information,
        Message = "OAuth state cleanup service stopped" )]
    internal static partial void LogServiceStopped( ILogger logger );

    #endregion
}
