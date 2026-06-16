using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// The current rate-limit status for a provider endpoint: whether it is limited, when it
/// may be retried, and how much of the window remains.
/// </summary>
/// <param name="IsRateLimited"><see langword="true"/> when the endpoint is currently rate-limited.</param>
/// <param name="RetryAfter">The absolute instant before which it should not be retried, when limited; otherwise <see langword="null"/>.</param>
/// <param name="TimeRemaining">
/// The remaining time in the rate-limit window as a snapshot, when limited; otherwise
/// <see langword="null"/>. Derived from the clock at read time and only valid at that moment.
/// </param>
public sealed record RateLimitState(

    [property: JsonPropertyName( "isRateLimited" )]
    bool IsRateLimited,

    [property: JsonPropertyName( "retryAfter" ),
    JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    DateTimeOffset? RetryAfter,

    [property: JsonPropertyName( "timeRemaining" ),
    JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    TimeSpan? TimeRemaining

);
