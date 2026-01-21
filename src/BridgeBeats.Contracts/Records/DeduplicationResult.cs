using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Result of attempting to acquire exclusive processing rights for a request.
/// </summary>
/// <param name="Acquired">Whether this instance successfully acquired the lock.</param>
/// <param name="AlreadyInFlight">Whether another instance is already processing this request.</param>
/// <param name="RequestKey">The unique key for the request that was checked.</param>
public sealed record DeduplicationResult(
    [property: JsonPropertyName( "acquired" )] bool Acquired,
    [property: JsonPropertyName( "alreadyInFlight" )] bool AlreadyInFlight,
    [property: JsonPropertyName( "requestKey" )] string RequestKey
);
