namespace BridgeBeats.Worker.CacheBootstrap.Logging;

/// <summary>
/// Stable numeric event identifiers for the CacheBootstrap worker's structured log messages (5500-5749).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with CacheBootstrap-specific EventIds.
/// Each constant is the <c>EventId</c> assigned to a corresponding
/// <see cref="Microsoft.Extensions.Logging.LoggerMessageAttribute"/> method. The background-service
/// messages occupy 5500–5549 and the startup messages occupy 5550–5574; values are part of the
/// logging contract and must not be changed once shipped.
/// </summary>
public static class LogEventIds {
    #region CacheBootstrapBackgroundService (5500-5549)

    /// <summary>A periodic (non-startup) bootstrap run failed and will retry at the next interval.</summary>
    public const int BootstrapPeriodicError = 5500;

    /// <summary>The bootstrap service is shutting down.</summary>
    public const int BootstrapShuttingDown = 5501;

    /// <summary>The Redis key count measured before a bootstrap run began.</summary>
    public const int RedisKeyCountBefore = 5502;

    /// <summary>Measuring the Redis key count before a run failed.</summary>
    public const int RedisKeyCountBeforeError = 5503;

    /// <summary>A bootstrap run is starting against the configured PDS and DID.</summary>
    public const int BootstrapStarting = 5504;

    /// <summary>Periodic progress update reporting how many records have been processed.</summary>
    public const int BootstrapProgress = 5505;

    /// <summary>Caching a single PDS record failed; the run continues with the next record.</summary>
    public const int CacheRecordError = 5506;

    /// <summary>The bootstrap run was cancelled by host shutdown.</summary>
    public const int BootstrapCancelled = 5507;

    /// <summary>A fatal error aborted the bootstrap run.</summary>
    public const int BootstrapFatalError = 5508;

    /// <summary>The Redis key count measured after a bootstrap run completed.</summary>
    public const int RedisKeyCountAfter = 5509;

    /// <summary>Measuring the Redis key count after a run failed.</summary>
    public const int RedisKeyCountAfterError = 5510;

    /// <summary>A bootstrap run completed, with timing, success/error counts, and key-count delta.</summary>
    public const int BootstrapCompleted = 5511;

    /// <summary>Writing the bootstrap status document to Redis failed.</summary>
    public const int StatusUpdateError = 5512;

    /// <summary>Reading the bootstrap status document from Redis failed.</summary>
    public const int StatusReadError = 5513;

    #endregion

    #region Program Startup (5550-5574)

    /// <summary>Startup diagnostic describing the Redis connection configuration and state.</summary>
    public const int RedisConnectionInfo = 5550;

    /// <summary>Result of the startup Redis write/read-back smoke test.</summary>
    public const int RedisWriteTest = 5551;

    /// <summary>The startup Redis write/read-back smoke test failed to verify.</summary>
    public const int RedisWriteVerificationFailed = 5552;

    /// <summary>Startup diagnostic reporting the Redis key count for an endpoint.</summary>
    public const int RedisKeyCount = 5553;

    #endregion
}
