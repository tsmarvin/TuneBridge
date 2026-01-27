using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Infrastructure.Identity;

/// <summary>
/// Background service that periodically cleans up expired ATProto OAuth state entries.
/// </summary>
public class OAuthStateCleanupService(
    IATProtoOAuthService oauthService,
    ILogger<OAuthStateCleanupService> logger
) : BackgroundService {

    private readonly IATProtoOAuthService _oauthService = oauthService;
    private readonly ILogger<OAuthStateCleanupService> _logger = logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours( 6 ); // Run every 6 hours

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        _logger.LogInformation( "OAuth state cleanup service started" );

        // Perform initial cleanup immediately
        try {
            _logger.LogInformation( "Running initial OAuth state cleanup..." );
            int removedCount = await _oauthService.CleanupExpiredStatesAsync( stoppingToken );
            _logger.LogInformation( "Initial OAuth state cleanup completed. Removed {Count} expired entries.", removedCount );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error during initial OAuth state cleanup" );
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay( _cleanupInterval, stoppingToken );

                _logger.LogInformation( "Running OAuth state cleanup..." );
                int removedCount = await _oauthService.CleanupExpiredStatesAsync( stoppingToken );
                _logger.LogInformation( "OAuth state cleanup completed. Removed {Count} expired entries.", removedCount );
            } catch (OperationCanceledException) {
                // Expected when the service is stopping
                break;
            } catch (Exception ex) {
                _logger.LogError( ex, "Error during OAuth state cleanup" );
            }
        }

        _logger.LogInformation( "OAuth state cleanup service stopped" );
    }
}
