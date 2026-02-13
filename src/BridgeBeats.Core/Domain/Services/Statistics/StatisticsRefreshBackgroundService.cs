using System.Threading.Channels;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Hosting;

namespace BridgeBeats.Core.Domain.Services;

/// <summary>
/// Background service that periodically refreshes statistics and responds to manual
/// trigger signals via a <see cref="Channel{T}"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="StatisticsRefreshBackgroundService"/> class.
/// </remarks>
/// <param name="statisticsService">The statistics service used to refresh data.</param>
/// <param name="settings">Configuration settings including cache duration and startup delay.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public sealed partial class StatisticsRefreshBackgroundService(
    StatisticsService statisticsService,
    StatisticsSettings settings,
    ILogger<StatisticsRefreshBackgroundService> logger
) : BackgroundService {

    /// <inheritdoc/>
    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        LogStarting( settings.StartupDelay, settings.CacheDuration );

        // Wait for dependent services (Redis, ATProto) to initialize
        await Task.Delay( settings.StartupDelay, stoppingToken );

        // Initial refresh on startup
        try {
            _ = await statisticsService.RefreshStatisticsAsync( true, stoppingToken );
            LogInitialRefreshComplete( );
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            return;
        } catch (Exception ex) {
            LogRefreshError( ex );
        }

        // Periodic refresh loop
        using PeriodicTimer timer = new( settings.CacheDuration );
        ChannelReader<bool> channelReader = statisticsService.RefreshTriggerReader;

        while (!stoppingToken.IsCancellationRequested) {
            try {
                // Wait for either the periodic timer or a manual trigger signal
                Task timerTask = timer.WaitForNextTickAsync( stoppingToken ).AsTask( );
                Task channelTask = channelReader.WaitToReadAsync( stoppingToken ).AsTask( );

                _ = await Task.WhenAny( timerTask, channelTask );

                if (stoppingToken.IsCancellationRequested) {
                    break;
                }

                // Drain any pending channel signals so we don't double-trigger
                while (channelReader.TryRead( out _ )) {
                    // discard
                }

                bool wasManualTrigger = channelTask.IsCompletedSuccessfully;
                if (wasManualTrigger) {
                    LogManualRefreshTriggered( );
                } else {
                    LogPeriodicRefreshTriggered( );
                }

                _ = await statisticsService.RefreshStatisticsAsync( wasManualTrigger, stoppingToken );
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogRefreshError( ex );
            }
        }

        LogStopped( );
    }
}
