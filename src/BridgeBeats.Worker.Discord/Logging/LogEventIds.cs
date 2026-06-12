namespace BridgeBeats.Worker.Discord.Logging;

/// <summary>
/// EventIds for Discord worker (6000-6249).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with Discord worker-specific EventIds.
/// </summary>
public static class LogEventIds {
    #region BridgeBeatsApiClient (6000-6049)

    /// <summary>Failed to call music lookup API.</summary>
    public const int LookupApiError = 6000;

    /// <summary>Failed to deserialize lookup response.</summary>
    public const int LookupDeserializeError = 6001;

    /// <summary>Failed to store card via API.</summary>
    public const int StoreCardApiError = 6002;

    /// <summary>Failed to deserialize store card response.</summary>
    public const int StoreCardDeserializeError = 6003;

    /// <summary>Music lookup API call timed out or was cancelled.</summary>
    public const int LookupApiTimeout = 6004;

    #endregion
}
