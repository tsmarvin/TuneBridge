namespace BridgeBeats.Worker.AppleMusic.Logging;

/// <summary>
/// Reserved holder for Apple Music worker log event identifiers (6250-6499).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with AppleMusic worker-specific EventIds.
/// </summary>
/// <remarks>
/// The Apple Music worker is a thin host that runs only the shared queue-processing
/// loop and the WorkerApi lookup endpoints, so it declares no bespoke log events of
/// its own. This class exists as the per-worker home for any future event ids and to
/// mirror the logging convention used by the other provider workers.
/// </remarks>
public static class LogEventIds {
    // AppleMusicQueueProcessor (6250-6299)
}
