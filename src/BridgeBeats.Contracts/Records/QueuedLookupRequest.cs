using System.Text.Json.Serialization;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// The queue payload for one provider's leg of a lookup. Carries everything a worker needs
/// to perform the lookup plus the saga and retry bookkeeping the queue uses to route and
/// re-deliver it. Implements <see cref="IQueueableRequest"/>.
/// </summary>
public sealed record QueuedLookupRequest : IQueueableRequest {

    /// <summary>The unique identifier of this queued request.</summary>
    [JsonPropertyName( "requestId" )]
    [JsonRequired]
    public required string RequestId { get; init; }

    /// <summary>The provider this leg targets.</summary>
    [JsonPropertyName( "provider" )]
    [JsonRequired]
    public required SupportedProviders Provider { get; init; }

    /// <summary>The lookup strategy to apply.</summary>
    [JsonPropertyName( "lookupType" )]
    [JsonRequired]
    public required LookupRequestType LookupType { get; init; }

    /// <summary>The raw lookup value (for example, an ISRC, UPC, URL, or id) for this leg.</summary>
    [JsonPropertyName( "lookupValue" )]
    [JsonRequired]
    public required string LookupValue { get; init; }

    /// <summary>The artist name for metadata lookups, when the lookup carries one; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName( "artist" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Artist { get; init; }

    /// <summary>The title for metadata lookups, when the lookup carries one; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName( "title" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Title { get; init; }

    /// <summary><see langword="true"/> when the lookup targets an album rather than a track.</summary>
    [JsonPropertyName( "isAlbum" )]
    public bool IsAlbum { get; init; }

    /// <summary>The identifier of the saga (job) this leg belongs to.</summary>
    [JsonPropertyName( "sagaId" )]
    [JsonRequired]
    public required string SagaId { get; init; }

    /// <summary>The absolute instant the request was originally created. Defaults to the current UTC time.</summary>
    [JsonPropertyName( "createdAt" )]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The number of times delivery of this request has been attempted.</summary>
    [JsonPropertyName( "attemptCount" )]
    public int AttemptCount { get; init; }

    /// <summary>The original endpoint that triggered the rate limit on a prior attempt, for tracking; otherwise <see langword="null"/>.</summary>
    [JsonPropertyName( "rateLimitedEndpoint" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? RateLimitedEndpoint { get; init; }

    /// <summary>
    /// The scheduling priority the request inherits from its originating lookup, persisted into
    /// the saga so secondary lookups inherit the origin's urgency. Defaults to
    /// <see cref="QueuePriority.Background"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="QueuePriority.Background"/> so in-flight messages serialized before
    /// this field existed are never promoted to interactive.
    /// </remarks>
    [JsonPropertyName( "originPriority" )]
    public QueuePriority OriginPriority { get; init; } = QueuePriority.Background;
}
