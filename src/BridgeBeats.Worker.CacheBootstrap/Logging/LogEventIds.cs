namespace BridgeBeats.Worker.CacheBootstrap.Logging;

/// <summary>
/// EventIds for CacheBootstrap worker (5500-5749).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with CacheBootstrap-specific EventIds.
/// </summary>
public static class LogEventIds {
    #region CacheBootstrapBackgroundService (5500-5549)

    /// <summary>Error during periodic cache bootstrap.</summary>
    public const int BootstrapPeriodicError = 5500;

    /// <summary>Cache bootstrap service is shutting down.</summary>
    public const int BootstrapShuttingDown = 5501;

    /// <summary>Redis key count before bootstrap.</summary>
    public const int RedisKeyCountBefore = 5502;

    /// <summary>Failed to get Redis key count before bootstrap.</summary>
    public const int RedisKeyCountBeforeError = 5503;

    /// <summary>Starting cache bootstrap from ATProto.</summary>
    public const int BootstrapStarting = 5504;

    /// <summary>Bootstrap progress update.</summary>
    public const int BootstrapProgress = 5505;

    /// <summary>Failed to cache a record.</summary>
    public const int CacheRecordError = 5506;

    /// <summary>Cache bootstrap was cancelled.</summary>
    public const int BootstrapCancelled = 5507;

    /// <summary>Fatal error during cache bootstrap.</summary>
    public const int BootstrapFatalError = 5508;

    /// <summary>Redis key count after bootstrap.</summary>
    public const int RedisKeyCountAfter = 5509;

    /// <summary>Failed to get Redis key count after bootstrap.</summary>
    public const int RedisKeyCountAfterError = 5510;

    /// <summary>Cache bootstrap completed.</summary>
    public const int BootstrapCompleted = 5511;

    /// <summary>Failed to update status in Redis.</summary>
    public const int StatusUpdateError = 5512;

    #endregion

    #region Program Startup (5550-5574)

    /// <summary>Redis connection information.</summary>
    public const int RedisConnectionInfo = 5550;

    /// <summary>Redis write test result.</summary>
    public const int RedisWriteTest = 5551;

    /// <summary>Redis write verification failed.</summary>
    public const int RedisWriteVerificationFailed = 5552;

    /// <summary>Redis server key count.</summary>
    public const int RedisKeyCount = 5553;

    #endregion
}
