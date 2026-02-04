using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Represents the current depth of a queue broken down by priority level.
/// </summary>
/// <param name="Interactive">Count of interactive (high priority) requests.</param>
/// <param name="Background">Count of background refresh requests.</param>
/// <param name="Bulk">Count of bulk processing requests.</param>
/// <param name="Total">Total count across all priority levels.</param>
public sealed record QueueDepth(
    [property: JsonPropertyName( "interactive" )]
    int Interactive,

    [property: JsonPropertyName( "background" )]
    int Background,

    [property: JsonPropertyName( "bulk" )]
    int Bulk,

    [property: JsonPropertyName( "total" )]
    int Total
);
