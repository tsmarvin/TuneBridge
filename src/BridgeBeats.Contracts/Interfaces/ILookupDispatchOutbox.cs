using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Transactional outbox for initial provider-leg dispatch. Staging atomically initializes saga
/// provider state and records the queue payload; dispatch atomically transfers that payload to the
/// provider stream and removes the outbox record.
/// </summary>
public interface ILookupDispatchOutbox {
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

    /// <summary>Relays a bounded batch of pending outbox items.</summary>
    Task<int> DispatchPendingAsync( int limit, CancellationToken cancellationToken = default );
}
