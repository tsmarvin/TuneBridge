namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Configuration settings for Spotify batch (bulk) lookup processing.
/// </summary>
/// <remarks>
/// Bind under <c>BridgeBeats:Spotify:Batch</c> in appsettings.json.
/// Size is the primary flush trigger (50 tracks / 20 albums fires immediately).
/// The linger age is a staleness backstop for low-volume streams, not a latency bound.
/// </remarks>
public sealed class SpotifyBatchSettings {

    private int _lingerMs = DefaultLingerMs;
    private int _requestFailureCooldownSeconds = DefaultRequestFailureCooldownSeconds;

    /// <summary>
    /// Configuration section key.
    /// </summary>
    public const string SectionKey = "BridgeBeats:Spotify:Batch";

    /// <summary>
    /// Default staleness backstop used when <see cref="LingerMs"/> is ≤ 0.
    /// Equal to 24 hours (86 400 000 ms). Size flush (50 tracks / 20 albums) is the
    /// primary trigger; this backstop drains low-volume streams that never reach the size threshold.
    /// </summary>
    public const int DefaultLingerMs = 86_400_000;

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
    /// Gets or sets the staleness backstop in milliseconds.
    /// A stream with fewer messages than the size threshold is flushed once the oldest pending
    /// entry reaches this age, preventing low-volume streams from waiting indefinitely.
    /// </summary>
    /// <remarks>
    /// Fires only when <c>0 &lt; count &lt; threshold</c> and the oldest entry is at least this old.
    /// Default: 86 400 000 ms (24 h). Values ≤ 0 are clamped to <see cref="DefaultLingerMs"/>.
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
    /// Exponential backoff per consecutive failure (doubles each time, capped at <see cref="MaxRequestFailureCooldownSeconds"/>).
    /// Resets on the next successful batch dispatch. Default: 5s. Values ≤ 0 clamped to <see cref="DefaultRequestFailureCooldownSeconds"/>.
    /// Config key: <c>BridgeBeats:Spotify:Batch:RequestFailureCooldownSeconds</c>.
    /// </remarks>
    public int RequestFailureCooldownSeconds {
        get => _requestFailureCooldownSeconds;
        set => _requestFailureCooldownSeconds = value > 0 ? value : DefaultRequestFailureCooldownSeconds;
    }
}
