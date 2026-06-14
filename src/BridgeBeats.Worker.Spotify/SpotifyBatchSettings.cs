namespace BridgeBeats.Worker.Spotify;

/// <summary>
/// Tunable options for the Spotify bulk-batch processing path, bound from configuration.
/// </summary>
/// <remarks>
/// These settings control the batch linger window and the request-level failure cooldown used by
/// <see cref="SpotifyBulkProcessorService"/>. Bound from <c>BridgeBeats:Spotify:Batch</c>. Size is
/// the primary flush trigger (50 tracks / 20 albums fires immediately); the linger age is a
/// staleness backstop for low-volume streams, not a latency bound. Both writable properties coerce
/// non-positive values back to their defaults so misconfiguration cannot disable batching or the
/// cooldown entirely.
/// </remarks>
public sealed class SpotifyBatchSettings {

    /// <summary>Backing field for <see cref="LingerMs"/>.</summary>
    private int _lingerMs = DefaultLingerMs;

    /// <summary>Backing field for <see cref="RequestFailureCooldownSeconds"/>.</summary>
    private int _requestFailureCooldownSeconds = DefaultRequestFailureCooldownSeconds;

    /// <summary>
    /// Configuration section key these settings are bound from (<c>BridgeBeats:Spotify:Batch</c>).
    /// </summary>
    public const string SectionKey = "BridgeBeats:Spotify:Batch";

    /// <summary>
    /// Default staleness backstop used when <see cref="LingerMs"/> is unset or non-positive.
    /// Equal to 24 hours (86 400 000 ms). The size flush (50 tracks / 20 albums) is the primary
    /// trigger; this very large default effectively disables time-based flushing, draining
    /// low-volume streams that never reach the size threshold.
    /// </summary>
    public const int DefaultLingerMs = 86_400_000;

    /// <summary>
    /// Default request-failure cooldown in seconds, used as the base for exponential backoff
    /// when <see cref="RequestFailureCooldownSeconds"/> is unset or non-positive.
    /// </summary>
    public const int DefaultRequestFailureCooldownSeconds = 5;

    /// <summary>
    /// Upper bound in seconds for the request-failure cooldown after exponential backoff,
    /// so repeated failures never park a bulk stream longer than this.
    /// </summary>
    public const int MaxRequestFailureCooldownSeconds = 60;

    /// <summary>
    /// Gets or sets the staleness backstop in milliseconds (the batch linger window). A stream with
    /// fewer messages than the size threshold is flushed once the oldest pending entry reaches this
    /// age, preventing low-volume streams from waiting indefinitely.
    /// </summary>
    /// <remarks>
    /// Fires only when <c>0 &lt; count &lt; threshold</c> and the oldest entry is at least this old.
    /// Default: 86 400 000 ms (24 h). Values ≤ 0 are clamped to <see cref="DefaultLingerMs"/>.
    /// Config key: <c>BridgeBeats:Spotify:Batch:LingerMs</c>.
    /// </remarks>
    /// <value>
    /// A positive number of milliseconds. Assigning a non-positive value resets it to
    /// <see cref="DefaultLingerMs"/>.
    /// </value>
    public int LingerMs {
        get => _lingerMs;
        set => _lingerMs = value > 0 ? value : DefaultLingerMs;
    }

    /// <summary>
    /// Gets or sets the initial flush-suppression cooldown in seconds after a bulk request failure
    /// (an empty-dict response indicating a network or auth error).
    /// </summary>
    /// <remarks>
    /// Exponential backoff per consecutive failure (doubles each time, capped at
    /// <see cref="MaxRequestFailureCooldownSeconds"/>). Resets on the next successful batch dispatch.
    /// Default: 5s. Values ≤ 0 clamped to <see cref="DefaultRequestFailureCooldownSeconds"/>.
    /// Config key: <c>BridgeBeats:Spotify:Batch:RequestFailureCooldownSeconds</c>.
    /// </remarks>
    /// <value>
    /// A positive number of seconds. Assigning a non-positive value resets it to
    /// <see cref="DefaultRequestFailureCooldownSeconds"/>.
    /// </value>
    public int RequestFailureCooldownSeconds {
        get => _requestFailureCooldownSeconds;
        set => _requestFailureCooldownSeconds = value > 0 ? value : DefaultRequestFailureCooldownSeconds;
    }
}
