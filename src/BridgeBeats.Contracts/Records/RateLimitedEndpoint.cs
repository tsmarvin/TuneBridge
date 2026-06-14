using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// A single rate-limited endpoint paired with the instant it may be retried.
/// </summary>
/// <param name="Endpoint">The API endpoint path that is currently rate-limited.</param>
/// <param name="RetryAfter">The absolute wall-clock instant before which the endpoint should not be retried. An instant, not a duration.</param>
public sealed record RateLimitedEndpoint(

    [property: JsonPropertyName( "endpoint" )]
    string Endpoint,

    [property: JsonPropertyName( "retryAfter" )]
    DateTimeOffset RetryAfter

);
