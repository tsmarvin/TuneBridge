using System.Text.Json.Serialization;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// State of a multi-provider lookup saga.
/// </summary>
/// <remarks>
/// A saga tracks the progress of a lookup across multiple providers,
/// enabling eventual consistency when some providers are rate-limited.
/// </remarks>
public sealed record LookupSagaState {
    /// <summary>
    /// Gets the unique identifier for this saga.
    /// </summary>
    [JsonPropertyName( "sagaId" )]
    [JsonRequired]
    public required string SagaId { get; init; }

    /// <summary>
    /// Gets the lookup key used for deduplication (e.g., "isrc:US1234567890").
    /// </summary>
    [JsonPropertyName( "lookupKey" )]
    [JsonRequired]
    public required string LookupKey { get; init; }

    /// <summary>
    /// Gets the type of lookup being performed.
    /// </summary>
    [JsonPropertyName( "lookupType" )]
    [JsonRequired]
    public required LookupRequestType LookupType { get; init; }

    /// <summary>
    /// Gets the lookup value (ISRC, UPC, URL, etc.).
    /// </summary>
    [JsonPropertyName( "lookupValue" )]
    [JsonRequired]
    public required string LookupValue { get; init; }

    /// <summary>
    /// Gets when this saga was created.
    /// </summary>
    [JsonPropertyName( "createdAt" )]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the state of each provider's lookup within this saga.
    /// </summary>
    [JsonPropertyName( "providerStates" )]
    public Dictionary<SupportedProviders, ProviderLookupState> ProviderStates { get; init; } = [];

    /// <summary>
    /// Gets the ATProto URI of the partial result, if one was written.
    /// </summary>
    [JsonPropertyName( "partialResultUri" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? PartialResultUri { get; init; }

    /// <summary>
    /// Gets the ATProto URI of the final result, if all providers have completed.
    /// </summary>
    [JsonPropertyName( "finalResultUri" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? FinalResultUri { get; init; }

    /// <summary>
    /// Gets whether this saga has partial results waiting for completion.
    /// </summary>
    [JsonPropertyName( "isPartial" )]
    public bool IsPartial { get; init; }

    /// <summary>
    /// Gets the provider that initiated this lookup (the source URL's provider).
    /// </summary>
    [JsonPropertyName( "initialProvider" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public SupportedProviders? InitialProvider { get; init; }

    /// <summary>
    /// Gets information about rate-limited providers for user notification.
    /// </summary>
    [JsonPropertyName( "rateLimitInfo" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public List<ProviderRateLimitInfo>? RateLimitInfo { get; init; }

    /// <summary>
    /// Gets the set of providers that have not yet completed their lookups.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<SupportedProviders> PendingProviders =>
        ProviderStates.Where( kv => !kv.Value.IsComplete ).Select( kv => kv.Key );

    /// <summary>
    /// Gets whether all providers have completed their lookups.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => ProviderStates.Count > 0 && ProviderStates.Values.All( s => s.IsComplete );
}
