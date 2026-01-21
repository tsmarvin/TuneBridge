using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Information about a rate-limited endpoint.
/// </summary>
/// <param name="Endpoint">The API endpoint path that is rate-limited.</param>
/// <param name="RetryAfter">When the rate limit for this endpoint expires.</param>
public sealed record RateLimitedEndpoint(
    [property: JsonPropertyName( "endpoint" )] string Endpoint,
    [property: JsonPropertyName( "retryAfter" )] DateTimeOffset RetryAfter
);
