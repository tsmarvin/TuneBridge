namespace BridgeBeats.Worker.Tidal.Logging;

/// <summary>
/// Reserved holder for Tidal worker log event identifiers (6500-6749).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with Tidal worker-specific EventIds.
/// </summary>
/// <remarks>
/// The Tidal worker is a thin host that runs only the shared queue-processing loop and
/// the WorkerApi lookup endpoints, so it declares no bespoke log events of its own. This
/// class exists as the per-worker home for any future event ids and to mirror the logging
/// convention used by the other provider workers.
/// </remarks>
public static class LogEventIds {
    // TidalQueueProcessor (6500-6549)
}
