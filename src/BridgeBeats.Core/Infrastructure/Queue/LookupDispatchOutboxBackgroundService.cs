using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Core.Infrastructure.Queue;

/// <summary>Continuously relays lookup dispatch outbox entries left behind by interrupted callers.</summary>
internal sealed partial class LookupDispatchOutboxBackgroundService(
    ILookupDispatchOutbox outbox,
    ILogger<LookupDispatchOutboxBackgroundService> logger
) : BackgroundService {
    private static readonly TimeSpan s_idleDelay = TimeSpan.FromSeconds( 1 );

    protected override async Task ExecuteAsync( CancellationToken stoppingToken ) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                int dispatched = await outbox.DispatchPendingAsync( 100, stoppingToken );
                if (dispatched == 0) {
                    await Task.Delay( s_idleDelay, stoppingToken );
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception ex) {
                LogRelayFailure( logger, ex );
                await Task.Delay( s_idleDelay, stoppingToken );
            }
        }
    }

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Queue.LookupDispatchOutboxRelayFailure,
        Level = LogLevel.Error,
        Message = "Lookup dispatch outbox relay failed; pending entries will be retried" )]
    private static partial void LogRelayFailure( ILogger logger, Exception exception );
}
