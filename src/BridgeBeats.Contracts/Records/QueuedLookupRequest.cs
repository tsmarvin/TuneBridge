using System.Text.Json.Serialization;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// A lookup request that has been queued for background processing.
/// </summary>
public sealed record QueuedLookupRequest : IQueueableRequest {
    /// <summary>
    /// Gets the unique identifier for this request.
    /// </summary>
    [JsonPropertyName( "requestId" )]
    [JsonRequired]
    public required string RequestId { get; init; }

    /// <summary>
    /// Gets the provider this request is for.
    /// </summary>
    [JsonPropertyName( "provider" )]
    [JsonRequired]
    public required SupportedProviders Provider { get; init; }

    /// <summary>
    /// Gets the type of lookup to perform.
    /// </summary>
    [JsonPropertyName( "lookupType" )]
    [JsonRequired]
    public required LookupRequestType LookupType { get; init; }

    /// <summary>
    /// Gets the lookup value (URL, ISRC, UPC, etc.).
    /// </summary>
    [JsonPropertyName( "lookupValue" )]
    [JsonRequired]
    public required string LookupValue { get; init; }

    /// <summary>
    /// Gets the artist name for metadata lookups.
    /// </summary>
    [JsonPropertyName( "artist" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Artist { get; init; }

    /// <summary>
    /// Gets the title for metadata lookups.
    /// </summary>
    [JsonPropertyName( "title" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Title { get; init; }

    /// <summary>
    /// Gets whether this is an album lookup.
    /// </summary>
    [JsonPropertyName( "isAlbum" )]
    public bool IsAlbum { get; init; }

    /// <summary>
    /// Gets the saga/job identifier this request belongs to.
    /// </summary>
    [JsonPropertyName( "sagaId" )]
    [JsonRequired]
    public required string SagaId { get; init; }

    /// <summary>
    /// Gets when this request was originally created.
    /// </summary>
    [JsonPropertyName( "createdAt" )]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the number of times this request has been attempted.
    /// </summary>
    [JsonPropertyName( "attemptCount" )]
    public int AttemptCount { get; init; }

    /// <summary>
    /// Gets the original endpoint that triggered the rate limit (for tracking).
    /// </summary>
    [JsonPropertyName( "rateLimitedEndpoint" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? RateLimitedEndpoint { get; init; }

    /// <summary>
    /// Gets the priority of the originating request, persisted into the saga so
    /// secondary lookups inherit the origin's urgency.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="QueuePriority.Background"/> so in-flight messages
    /// serialized before this field existed are never promoted to interactive.
    /// </remarks>
    [JsonPropertyName( "originPriority" )]
    public QueuePriority OriginPriority { get; init; } = QueuePriority.Background;
}
