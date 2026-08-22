using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Transactional outbox for initial provider-leg dispatch. Staging atomically initializes saga
/// provider state and records the queue payload; dispatch atomically transfers that payload to the
/// provider stream. Published envelopes remain durable until completion so lost deliveries can be
/// re-driven after the visibility window.
/// </summary>
public interface ILookupDispatchOutbox {
    /// <summary>
    /// Atomically stages every initial provider dispatch for one saga generation. Either the whole
    /// expected provider set is initialized and staged, or none of it is changed.
    /// </summary>
    Task<IReadOnlyDictionary<SupportedProviders, ProviderDispatchStageOutcome>> StageBatchAsync(
        IReadOnlyList<QueuedLookupRequest> requests,
        QueuePriority priority,
        CancellationToken cancellationToken = default );

    /// <summary>Stages one provider dispatch while the supplied saga instance token still owns it.</summary>
    Task<ProviderDispatchStageOutcome> StageAsync(
        QueuedLookupRequest request,
        QueuePriority priority,
        CancellationToken cancellationToken = default );

    /// <summary>Attempts to atomically publish one staged provider dispatch.</summary>
    Task<bool> DispatchAsync(
        string sagaId,
        SupportedProviders provider,
        CancellationToken cancellationToken = default );

    /// <summary>Relays a bounded batch of pending items and stale published deliveries.</summary>
    Task<int> DispatchPendingAsync( int limit, CancellationToken cancellationToken = default );
}
