using System.Text.Json.Serialization;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// The state of a single provider's leg within a lookup saga. Completion and success are
/// independent: a leg can be complete and failed at once (complete, not successful, with an
/// error message). Part of <see cref="LookupSagaState.ProviderStates"/>.
/// </summary>
/// <param name="Provider">The provider this leg tracks.</param>
/// <param name="IsComplete"><see langword="true"/> when the leg has finished, whether it succeeded or failed.</param>
/// <param name="IsSuccess"><see langword="true"/> when the leg produced a usable result. Independent of <paramref name="IsComplete"/>.</param>
/// <param name="ResultJson">The serialized provider result when the leg succeeded; otherwise <see langword="null"/>.</param>
/// <param name="CompletedAt">The absolute instant the leg completed, when known; otherwise <see langword="null"/>.</param>
/// <param name="ErrorMessage">The failure detail when the leg failed; otherwise <see langword="null"/>.</param>
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
