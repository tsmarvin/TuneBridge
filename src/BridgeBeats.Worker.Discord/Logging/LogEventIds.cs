namespace BridgeBeats.Worker.Discord.Logging;

/// <summary>
/// Stable numeric log event ids for the Discord worker, in the 6000-6249 range, grouped by source
/// component. The range continues from the shared
/// <see cref="Core.Infrastructure.Logging.LogEventIds"/> defined for the rest of the system. Each
/// constant backs a <c>[LoggerMessage]</c> declaration; the values are part of the worker's logging
/// contract, so log consumers can filter and alert on them. Do not reassign existing values.
/// </summary>
public static class LogEventIds {
    #region BridgeBeatsApiClient (6000-6049)

    /// <summary>A call to the Web music-lookup API failed at the HTTP transport level.</summary>
    public const int LookupApiError = 6000;

    /// <summary>The body returned by the music-lookup API could not be deserialized.</summary>
    public const int LookupDeserializeError = 6001;

    /// <summary>A call to the Web card-store API failed at the HTTP transport level.</summary>
    public const int StoreCardApiError = 6002;

    /// <summary>The body returned by the card-store API could not be deserialized.</summary>
    public const int StoreCardDeserializeError = 6003;

    /// <summary>The music-lookup API call timed out or was cancelled before a response arrived.</summary>
    public const int LookupApiTimeout = 6004;

    #endregion
}
