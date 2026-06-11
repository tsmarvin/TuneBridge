namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Configuration settings for Spotify batch (bulk) lookup processing.
/// </summary>
/// <remarks>
/// Bind under <c>BridgeBeats:Spotify:Batch</c> in appsettings.json.
/// Example:
/// <code>
/// "BridgeBeats": {
///   "Spotify": {
///     "Batch": {
///       "LingerMs": 500
///     }
///   }
/// }
/// </code>
/// </remarks>
public sealed class SpotifyBatchSettings {

    private int _lingerMs = DefaultLingerMs;
    private int _requestFailureCooldownSeconds = DefaultRequestFailureCooldownSeconds;

    /// <summary>
    /// Configuration section key.
    /// </summary>
    public const string SectionKey = "BridgeBeats:Spotify:Batch";

    /// <summary>Default linger value used when <see cref="LingerMs"/> is ≤ 0.</summary>
    public const int DefaultLingerMs = 500;

    /// <summary>
    /// Default initial cooldown after a bulk request failure before the stream is flushed again.
    /// See <see cref="RequestFailureCooldownSeconds"/>.
    /// </summary>
    public const int DefaultRequestFailureCooldownSeconds = 5;

    /// <summary>
    /// Maximum cooldown after consecutive bulk request failures.
    /// See <see cref="RequestFailureCooldownSeconds"/>.
    /// </summary>
    public const int MaxRequestFailureCooldownSeconds = 60;

    /// <summary>
    /// Gets or sets the maximum age in milliseconds that a message in the type-specific bulk
    /// stream may wait before the batch is flushed, regardless of whether the count threshold
    /// has been reached.
    /// </summary>
    /// <remarks>
    /// This is the "size-OR-age" age half of the flush policy. A batch of even 1 message is
    /// flushed after this many milliseconds, preventing indefinite delays on low-volume streams.
    /// Default: 500ms — matches the poll interval so low-volume messages are flushed within
    /// one extra poll cycle. Tune upward if API call overhead outweighs the latency benefit.
    /// Values ≤ 0 are clamped to <see cref="DefaultLingerMs"/> (500ms).
    /// Config key: <c>BridgeBeats:Spotify:Batch:LingerMs</c>.
    /// </remarks>
    public int LingerMs {
        get => _lingerMs;
        set => _lingerMs = value > 0 ? value : DefaultLingerMs;
    }

    /// <summary>
    /// Gets or sets the initial flush-suppression cooldown in seconds after a bulk request failure
    /// (empty-dict response indicating a network or auth error).
    /// </summary>
    /// <remarks>
    /// On the first consecutive failure the flush is suppressed for this many seconds.
    /// Each subsequent consecutive failure doubles the cooldown (exponential backoff),
    /// up to <see cref="MaxRequestFailureCooldownSeconds"/> (60s).
    /// The cooldown resets on the next successful batch dispatch.
    /// <para>
    /// Default: 5s. Tune upward on slow-recovering infra or downward if aggressive retry
    /// recovery is preferred. Values ≤ 0 are clamped to <see cref="DefaultRequestFailureCooldownSeconds"/>.
    /// </para>
    /// Config key: <c>BridgeBeats:Spotify:Batch:RequestFailureCooldownSeconds</c>.
    /// </remarks>
    public int RequestFailureCooldownSeconds {
        get => _requestFailureCooldownSeconds;
        set => _requestFailureCooldownSeconds = value > 0 ? value : DefaultRequestFailureCooldownSeconds;
    }
}
