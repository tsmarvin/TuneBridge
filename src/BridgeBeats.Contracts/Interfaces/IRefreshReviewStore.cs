using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Tracks stale-record refresh context and exposes terminal zero-result refreshes for review.
/// </summary>
public interface IRefreshReviewStore {
    /// <summary>Registers the PDS source record for a refresh saga until that saga completes.</summary>
    Task RegisterPendingAsync( RefreshReviewEntry entry, CancellationToken cancellationToken = default );

    /// <summary>Moves a registered refresh into the review queue after a zero-result completion.</summary>
    Task MarkUnresolvedAsync( string sagaId, string reason, CancellationToken cancellationToken = default );

    /// <summary>Clears pending refresh context after a successful completion.</summary>
    Task CompleteAsync( string sagaId, CancellationToken cancellationToken = default );

    /// <summary>Lists all records currently awaiting operator review.</summary>
    Task<IReadOnlyList<RefreshReviewEntry>> GetUnresolvedAsync( CancellationToken cancellationToken = default );

    /// <summary>Removes a reviewed entry only while its displayed saga and source CID still match.</summary>
    Task<bool> DeleteUnresolvedAsync(
        string sourceRecordUri,
        string expectedSagaId,
        string expectedSourceRecordCid,
        CancellationToken cancellationToken = default );
}
