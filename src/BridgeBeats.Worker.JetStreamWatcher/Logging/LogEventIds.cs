namespace BridgeBeats.Worker.JetStreamWatcher.Logging;

/// <summary>
/// EventIds for JetStreamWatcher worker (5750-5999).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with JetStreamWatcher-specific EventIds.
/// </summary>
public static class LogEventIds {
    #region JetStreamWatcherService (5750-5799)

    /// <summary>Service is starting.</summary>
    public const int WatcherStarting = 5750;

    /// <summary>Watching for music links description.</summary>
    public const int WatchingForLinks = 5751;

    /// <summary>Error in Jetstream connection.</summary>
    public const int ConnectionError = 5752;

    /// <summary>Service has stopped.</summary>
    public const int WatcherStopped = 5753;

    /// <summary>Error processing Jetstream record.</summary>
    public const int RecordProcessingError = 5754;

    /// <summary>Connected to Jetstream.</summary>
    public const int Connected = 5755;

    /// <summary>Disconnected from Jetstream.</summary>
    public const int Disconnected = 5756;

    /// <summary>Failed to enqueue music link.</summary>
    public const int EnqueueError = 5757;

    #endregion
}
