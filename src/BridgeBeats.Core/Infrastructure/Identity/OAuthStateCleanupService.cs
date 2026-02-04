using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Background service that periodically cleans up expired ATProto OAuth state entries.
/// </summary>
public partial class OAuthStateCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<OAuthStateCleanupService> logger
) : BackgroundService {

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<OAuthStateCleanupService> _logger = logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours( 6 ); // Run every 6 hours

    /// <inheritdoc/>
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
    /// Creates a scope and performs the cleanup operation.
    /// </summary>
    private async Task<int> PerformCleanupAsync( CancellationToken cancellationToken ) {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope( );
        IATProtoOAuthService oauthService = scope.ServiceProvider.GetRequiredService<IATProtoOAuthService>( );
        return await oauthService.CleanupExpiredStatesAsync( cancellationToken );
    }

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceStarted,
        Level = LogLevel.Information,
        Message = "OAuth state cleanup service started" )]
    internal static partial void LogServiceStarted( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceInitialCleanup,
        Level = LogLevel.Information,
        Message = "Running initial OAuth state cleanup..." )]
    internal static partial void LogRunningInitialCleanup( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceInitialCleanupCompleted,
        Level = LogLevel.Information,
        Message = "Initial OAuth state cleanup completed. Removed {Count} expired entries." )]
    internal static partial void LogInitialCleanupCompleted( ILogger logger, int count );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceInitialCleanupError,
        Level = LogLevel.Error,
        Message = "Error during initial OAuth state cleanup" )]
    internal static partial void LogInitialCleanupError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceRunningCleanup,
        Level = LogLevel.Information,
        Message = "Running OAuth state cleanup..." )]
    internal static partial void LogRunningCleanup( ILogger logger );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceCleanupCompleted,
        Level = LogLevel.Information,
        Message = "OAuth state cleanup completed. Removed {Count} expired entries." )]
    internal static partial void LogCleanupCompleted( ILogger logger, int count );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceCleanupError,
        Level = LogLevel.Error,
        Message = "Error during OAuth state cleanup" )]
    internal static partial void LogCleanupError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Identity.OAuthStateCleanupServiceStopped,
        Level = LogLevel.Information,
        Message = "OAuth state cleanup service stopped" )]
    internal static partial void LogServiceStopped( ILogger logger );

    #endregion
}
