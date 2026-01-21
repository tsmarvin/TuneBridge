using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Configuration for priority weighting in queue processing.
/// </summary>
/// <remarks>
/// Weights determine the relative frequency at which requests from each priority
/// level are dequeued. Higher weights mean more frequent processing.
/// </remarks>
public sealed record PriorityWeights {
    /// <summary>
    /// Gets the weight for interactive (user-initiated) requests.
    /// </summary>
    [JsonPropertyName( "interactive" )]
    public int Interactive { get; init; } = 5;

    /// <summary>
    /// Gets the weight for background refresh requests.
    /// </summary>
    [JsonPropertyName( "background" )]
    public int Background { get; init; } = 2;

    /// <summary>
    /// Gets the weight for bulk processing requests.
    /// </summary>
    [JsonPropertyName( "bulk" )]
    public int Bulk { get; init; } = 1;
}
