using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// The current rate limit state for a provider endpoint.
/// </summary>
/// <param name="IsRateLimited">Whether the endpoint is currently rate-limited.</param>
/// <param name="RetryAfter">When the rate limit expires, if rate-limited.</param>
/// <param name="TimeRemaining">Time until the rate limit expires, if rate-limited.</param>
public sealed record RateLimitState(
    [property: JsonPropertyName( "isRateLimited" )] bool IsRateLimited,
    [property: JsonPropertyName( "retryAfter" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] DateTimeOffset? RetryAfter,
    [property: JsonPropertyName( "timeRemaining" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )] TimeSpan? TimeRemaining
);
