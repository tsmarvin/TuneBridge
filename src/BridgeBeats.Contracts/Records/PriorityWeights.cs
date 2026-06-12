using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Legacy configuration record retained for backwards-compatible settings files.
/// </summary>
/// <remarks>
/// These weight values no longer drive weighted-random stream selection. Queue ordering is
/// now deterministic: interactive-first with a bounded aging escape hatch controlled by
/// <see cref="QueueSettings.InteractiveAgingInterval"/>. The fields are preserved so that
/// existing configuration files do not produce unknown-property warnings on deserialization.
/// </remarks>
public sealed record PriorityWeights {
    /// <summary>
    /// Gets the weight for interactive (user-initiated) requests. Retained for configuration
    /// backwards compatibility; no longer used for stream ordering decisions.
    /// </summary>
    [JsonPropertyName( "interactive" )]
    public int Interactive { get; init; } = 5;

    /// <summary>
    /// Gets the weight for background refresh requests. Retained for configuration
    /// backwards compatibility; no longer used for stream ordering decisions.
    /// </summary>
    [JsonPropertyName( "background" )]
    public int Background { get; init; } = 2;

    /// <summary>
    /// Gets the weight for bulk processing requests. Retained for configuration
    /// backwards compatibility; no longer used for stream ordering decisions.
    /// </summary>
    [JsonPropertyName( "bulk" )]
    public int Bulk { get; init; } = 1;
}
