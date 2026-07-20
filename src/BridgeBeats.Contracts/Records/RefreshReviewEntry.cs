using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Review metadata for a stale PDS record whose refresh completed without any provider result.
/// </summary>
public sealed record RefreshReviewEntry {
    /// <summary>The AT-URI of the source PDS record.</summary>
    public required string SourceRecordUri { get; init; }

    /// <summary>
    /// The source record CID observed when the refresh was registered. Destructive disposition uses
    /// this value as an ATProto compare-and-swap guard so a newer record revision cannot be deleted.
    /// </summary>
    public string? SourceRecordCid { get; init; }

    /// <summary>The refresh saga that attempted to resolve the record.</summary>
    public required string SagaId { get; init; }

    /// <summary>The canonical lookup type used to identify the refresh saga.</summary>
    public required LookupRequestType LookupType { get; init; }

    /// <summary>The canonical lookup value used to identify the refresh saga.</summary>
    public required string LookupValue { get; init; }

    /// <summary>Whether the source record represents an album.</summary>
    public bool IsAlbum { get; init; }

    /// <summary>The last-known provider results from the source PDS record.</summary>
    public MediaLinkResult? SourceResult { get; init; }

    /// <summary>When the refresh was first registered.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the refresh became reviewable; null while it is still pending.</summary>
    public DateTimeOffset? FailedAt { get; init; }

    /// <summary>Why the record was placed in the review queue.</summary>
    public string? FailureReason { get; init; }
}
