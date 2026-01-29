using System.Text.Json.Serialization;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// The lookup state for a single provider within a saga.
/// </summary>
/// <param name="Provider">The music provider.</param>
/// <param name="IsComplete">Whether the lookup has completed (success or failure).</param>
/// <param name="IsSuccess">Whether the lookup completed successfully.</param>
/// <param name="ResultJson">The serialized result, if successful.</param>
/// <param name="CompletedAt">When the lookup completed.</param>
/// <param name="ErrorMessage">Error message if the lookup failed.</param>
public sealed record ProviderLookupState(

    [property: JsonPropertyName( "provider" )]
    SupportedProviders Provider,

    [property: JsonPropertyName( "isComplete" )]
    bool IsComplete,

    [property: JsonPropertyName( "isSuccess" )]
    bool IsSuccess,

    [property: JsonPropertyName( "resultJson" ),
    JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    string? ResultJson,

    [property: JsonPropertyName( "completedAt" ),
    JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    DateTimeOffset? CompletedAt,

    [property: JsonPropertyName( "errorMessage" ),
    JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    string? ErrorMessage

);
