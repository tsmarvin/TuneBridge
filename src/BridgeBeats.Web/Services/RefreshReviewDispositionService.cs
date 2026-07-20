using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Web.Services;

/// <summary>
/// Application service that keeps destructive PDS disposition, Redis cleanup, and audit logging in
/// one policy boundary rather than spreading the workflow across MVC actions.
/// </summary>
public sealed partial class RefreshReviewDispositionService(
    IRefreshReviewStore reviewStore,
    IATProtoStorageService atProtoStorage,
    ILogger<RefreshReviewDispositionService> logger
) : IRefreshReviewDispositionService {

    /// <inheritdoc/>
    public async Task<RefreshReviewDispositionOutcome> DeleteAsync(
        string sourceRecordUri,
        string expectedSagaId,
        string expectedSourceRecordCid,
        string actorId,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( sourceRecordUri );
        ArgumentException.ThrowIfNullOrWhiteSpace( expectedSagaId );
        ArgumentException.ThrowIfNullOrWhiteSpace( expectedSourceRecordCid );
        ArgumentException.ThrowIfNullOrWhiteSpace( actorId );

        IReadOnlyList<RefreshReviewEntry> entries = await reviewStore.GetUnresolvedAsync( cancellationToken );
        RefreshReviewEntry? entry = entries.FirstOrDefault( candidate => string.Equals(
            candidate.SourceRecordUri,
            sourceRecordUri,
            StringComparison.Ordinal ) );

        if (entry is null) {
            LogDisposition( logger, actorId, sourceRecordUri, null, "review-entry-not-found" );
            return RefreshReviewDispositionOutcome.ReviewEntryNotFound;
        }

        if (string.IsNullOrWhiteSpace( entry.SourceRecordCid )) {
            LogDisposition( logger, actorId, sourceRecordUri, null, "revision-unavailable" );
            return RefreshReviewDispositionOutcome.RevisionUnavailable;
        }

        if (!string.Equals( entry.SagaId, expectedSagaId, StringComparison.Ordinal )
            || !string.Equals( entry.SourceRecordCid, expectedSourceRecordCid, StringComparison.Ordinal )) {
            LogDisposition( logger, actorId, sourceRecordUri, expectedSourceRecordCid, "review-entry-changed" );
            return RefreshReviewDispositionOutcome.ReviewEntryChanged;
        }

        try {
            // Write intent before crossing the irreversible PDS boundary. The terminal outcome is
            // logged below, giving the audit trail both the actor's request and its result.
            LogDisposition( logger, actorId, sourceRecordUri, expectedSourceRecordCid, "delete-requested" );
            MediaLinkDeleteOutcome deleteOutcome = await atProtoStorage.DeleteMediaLinkResultAsync(
                sourceRecordUri,
                expectedSourceRecordCid,
                cancellationToken );

            // Deleted, already-absent, and changed records all make the captured review entry
            // obsolete. In the changed case the newer PDS revision is deliberately preserved.
            _ = await reviewStore.DeleteUnresolvedAsync(
                sourceRecordUri,
                expectedSagaId,
                expectedSourceRecordCid,
                cancellationToken );

            (RefreshReviewDispositionOutcome outcome, string auditOutcome) = deleteOutcome switch {
                MediaLinkDeleteOutcome.Deleted => (RefreshReviewDispositionOutcome.Deleted, "deleted"),
                MediaLinkDeleteOutcome.NotFound => (RefreshReviewDispositionOutcome.AlreadyAbsent, "already-absent"),
                MediaLinkDeleteOutcome.RevisionConflict => (RefreshReviewDispositionOutcome.RevisionChanged, "revision-changed"),
                _ => throw new InvalidOperationException( $"Unexpected delete outcome {deleteOutcome}." )
            };

            LogDisposition( logger, actorId, sourceRecordUri, entry.SourceRecordCid, auditOutcome );
            return outcome;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogDispositionFailed( logger, ex, actorId, sourceRecordUri, entry.SourceRecordCid );
            throw;
        }
    }

    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.RefreshReviewDisposition,
        Level = LogLevel.Information,
        Message = "Refresh-review disposition by {ActorId} for {SourceRecordUri} at CID {SourceRecordCid}: {Outcome}" )]
    private static partial void LogDisposition(
        ILogger logger,
        string actorId,
        string sourceRecordUri,
        string? sourceRecordCid,
        string outcome );

    [LoggerMessage(
        EventId = Logging.LogEventIds.Controllers.RefreshReviewDispositionFailed,
        Level = LogLevel.Error,
        Message = "Refresh-review disposition by {ActorId} failed for {SourceRecordUri} at CID {SourceRecordCid}" )]
    private static partial void LogDispositionFailed(
        ILogger logger,
        Exception ex,
        string actorId,
        string sourceRecordUri,
        string sourceRecordCid );
}
