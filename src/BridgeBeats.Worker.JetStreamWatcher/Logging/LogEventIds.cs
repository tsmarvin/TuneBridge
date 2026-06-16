namespace BridgeBeats.Worker.JetStreamWatcher.Logging;

/// <summary>
/// Stable numeric log event ids for the JetStream watcher, grouped by source component (5750-5999).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with JetStreamWatcher-specific EventIds.
/// Each constant backs a <c>[LoggerMessage]</c> declaration; the values are part of the worker's logging
/// contract, so log consumers can filter and alert on them. Do not reassign existing values.
/// </summary>
public static class LogEventIds {
    #region JetStreamWatcherService (5750-5799)

    /// <summary>The watcher service is starting up.</summary>
    public const int WatcherStarting = 5750;

    /// <summary>The watcher has begun scanning posts, reposts, and quote posts for music links.</summary>
    public const int WatchingForLinks = 5751;

    /// <summary>The Jetstream connection failed; the watcher will reconnect after a backoff.</summary>
    public const int ConnectionError = 5752;

    /// <summary>The watcher has stopped, typically in response to shutdown.</summary>
    public const int WatcherStopped = 5753;

    /// <summary>An unexpected error occurred while processing a single Jetstream record.</summary>
    public const int RecordProcessingError = 5754;

    /// <summary>The watcher has connected to the Jetstream firehose.</summary>
    public const int Connected = 5755;

    /// <summary>The watcher has disconnected from the Jetstream firehose.</summary>
    public const int Disconnected = 5756;

    /// <summary>Enqueuing a discovered music link to the provider queue failed.</summary>
    public const int EnqueueError = 5757;

    #endregion
}
