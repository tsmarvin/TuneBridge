using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// A point-in-time snapshot of how many items are queued in each priority lane, plus the
/// total across all lanes.
/// </summary>
/// <param name="Interactive">The number of items in the interactive lane.</param>
/// <param name="Background">The number of items in the background lane.</param>
/// <param name="Bulk">The number of items in the bulk lane.</param>
/// <param name="Total">The total number of queued items across all lanes.</param>
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
