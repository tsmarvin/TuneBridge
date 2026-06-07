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

        // Hoist both wait-tasks outside the loop. PeriodicTimer allows only one outstanding
        // WaitForNextTickAsync waiter at a time; recreating it inside the loop after channelTask
        // wins WhenAny abandons the in-flight timerTask, causing InvalidOperationException on the
        // next iteration.
        Task timerTask = timer.WaitForNextTickAsync( stoppingToken ).AsTask( );
        // Typed Task<bool>: WaitToReadAsync returns false when the channel is permanently closed
        // (writer completed). Treat false or a faulted task as terminal — log and break rather
        // than spinning forever on a channel that will never produce another signal.
        Task<bool> channelTask = channelReader.WaitToReadAsync( stoppingToken ).AsTask( );

        while (!stoppingToken.IsCancellationRequested) {
            try {
                _ = await Task.WhenAny( timerTask, channelTask );

                if (stoppingToken.IsCancellationRequested) {
                    break;
                }

                // If the channel task completed, check whether the channel is still open.
                // Guard against reading .Result on a canceled task: if the stopping token fires
                // between the IsCancellationRequested check above and here, channelTask may be
                // in the Canceled state — accessing .Result would throw AggregateException and
                // produce a spurious LogRefreshError at shutdown. Only read .Result when the task
                // completed successfully.
                if (channelTask.IsCompleted) {
                    if (channelTask.IsFaulted || (channelTask.IsCompletedSuccessfully && !channelTask.Result)) {
                        LogChannelCompleted( channelTask.Exception?.GetBaseException( ) );
                        break;
                    }
                }

                bool wasManualTrigger = channelTask.IsCompletedSuccessfully;

                // Drain any pending channel signals so we don't double-trigger
                while (channelReader.TryRead( out _ )) {
                    // discard
                }

                // Re-arm ONLY completed waiters before calling RefreshStatisticsAsync so a refresh
                // exception cannot leave a completed task un-rearmed (which would busy-loop WhenAny).
                if (timerTask.IsCompleted) {
                    timerTask = timer.WaitForNextTickAsync( stoppingToken ).AsTask( );
                }
                if (channelTask.IsCompleted) {
                    channelTask = channelReader.WaitToReadAsync( stoppingToken ).AsTask( );
                }

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
                // Guard the backoff delay: if the token fires during the wait, Task.Delay throws
                // OperationCanceledException which the sibling catch cannot intercept (already
                // unwound), so LogStopped would be skipped. Catch it here and break instead.
                try {
                    await Task.Delay( TimeSpan.FromSeconds( 5 ), stoppingToken );
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }

        LogStopped( );
    }
}
