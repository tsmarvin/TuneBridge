using System.Text.Json.Serialization;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Result of a lookup operation, including partial result status and rate limit information.
/// </summary>
public sealed record LookupResult {
    /// <summary>
    /// Gets the media link result, or null if no providers returned data.
    /// </summary>
    [JsonPropertyName( "result" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public MediaLinkResult? Result { get; init; }

    /// <summary>
    /// Gets whether this is a partial result with pending providers.
    /// </summary>
    [JsonPropertyName( "isPartial" )]
    public bool IsPartial { get; init; }

    /// <summary>
    /// Gets the saga ID for tracking completion of pending providers.
    /// </summary>
    [JsonPropertyName( "sagaId" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? SagaId { get; init; }

    /// <summary>
    /// Gets information about rate-limited providers, if any.
    /// </summary>
    [JsonPropertyName( "rateLimitedProviders" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public IReadOnlyList<ProviderRateLimitInfo>? RateLimitedProviders { get; init; }
}

/// <summary>
/// Information about a rate-limited provider for user notification.
/// </summary>
/// <param name="Provider">The provider that is rate-limited.</param>
/// <param name="RetryAfter">When the rate limit expires.</param>
/// <param name="Endpoint">The API endpoint that was rate-limited.</param>
public sealed record ProviderRateLimitInfo(
    [property: JsonPropertyName( "provider" )] SupportedProviders Provider,
    [property: JsonPropertyName( "retryAfter" )] DateTimeOffset RetryAfter,
    [property: JsonPropertyName( "endpoint" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] string? Endpoint
);
