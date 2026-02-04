namespace BridgeBeats.Worker.SagaCoordinator.Logging;

/// <summary>
/// EventIds for SagaCoordinator worker (5000-5249).
/// Extends <see cref="Core.Infrastructure.Logging.LogEventIds"/> with SagaCoordinator-specific EventIds.
/// </summary>
public static class LogEventIds {
    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogStarting"/>.
    /// </summary>
    public const int Starting = 5000;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogReceivedSagaCompletionEvent"/>.
    /// </summary>
    public const int ReceivedSagaCompletionEvent = 5001;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaCompletionError"/>.
    /// </summary>
    public const int SagaCompletionError = 5002;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSubscribedToChannel"/>.
    /// </summary>
    public const int SubscribedToChannel = 5003;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogReceivedLookupCompletionEvent"/>.
    /// </summary>
    public const int ReceivedLookupCompletionEvent = 5004;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogIgnoringChannel"/>.
    /// </summary>
    public const int IgnoringChannel = 5005;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogLookupCompletionError"/>.
    /// </summary>
    public const int LookupCompletionError = 5006;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSubscribedToPattern"/>.
    /// </summary>
    public const int SubscribedToPattern = 5007;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFullyInitialized"/>.
    /// </summary>
    public const int FullyInitialized = 5008;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogStartingPollingCycle"/>.
    /// </summary>
    public const int StartingPollingCycle = 5009;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogPollingError"/>.
    /// </summary>
    public const int PollingError = 5010;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogCompletedPollingCycle"/>.
    /// </summary>
    public const int CompletedPollingCycle = 5011;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogStopping"/>.
    /// </summary>
    public const int Stopping = 5012;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogProcessingSagaCompletion"/>.
    /// </summary>
    public const int ProcessingSagaCompletion = 5013;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaNotFound"/>.
    /// </summary>
    public const int SagaNotFound = 5014;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaState"/>.
    /// </summary>
    public const int SagaState = 5015;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaNotComplete"/>.
    /// </summary>
    public const int SagaNotComplete = 5016;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaAlreadyFinalized"/>.
    /// </summary>
    public const int SagaAlreadyFinalized = 5017;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFailedToProcessSaga"/>.
    /// </summary>
    public const int FailedToProcessSaga = 5018;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogProcessingLookupCompletion"/>.
    /// </summary>
    public const int ProcessingLookupCompletion = 5019;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaNotFoundForLookup"/>.
    /// </summary>
    public const int SagaNotFoundForLookup = 5020;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSagaStateForLookup"/>.
    /// </summary>
    public const int SagaStateForLookup = 5021;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFailedLookupCompletion"/>.
    /// </summary>
    public const int FailedLookupCompletion = 5022;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogPollingForSagas"/>.
    /// </summary>
    public const int PollingForSagas = 5023;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogNoUnfinalizedSagas"/>.
    /// </summary>
    public const int NoUnfinalizedSagas = 5024;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFoundUnfinalizedSagas"/>.
    /// </summary>
    public const int FoundUnfinalizedSagas = 5025;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogWritingPartialDuringPolling"/>.
    /// </summary>
    public const int WritingPartialDuringPolling = 5026;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFinalizingDuringPolling"/>.
    /// </summary>
    public const int FinalizingDuringPolling = 5027;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFailedDuringPolling"/>.
    /// </summary>
    public const int FailedDuringPolling = 5028;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogQueryError"/>.
    /// </summary>
    public const int QueryError = 5029;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogAssemblingFinalResult"/>.
    /// </summary>
    public const int AssemblingFinalResult = 5030;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogNoSuccessfulResults"/>.
    /// </summary>
    public const int NoSuccessfulResults = 5031;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogWroteFinalToAtProto"/>.
    /// </summary>
    public const int WroteFinalToAtProto = 5032;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogCachedFinalResult"/>.
    /// </summary>
    public const int CachedFinalResult = 5033;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSuccessfullyFinalized"/>.
    /// </summary>
    public const int SuccessfullyFinalized = 5034;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFailedToWriteFinal"/>.
    /// </summary>
    public const int FailedToWriteFinal = 5035;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogAssemblingPartialResult"/>.
    /// </summary>
    public const int AssemblingPartialResult = 5036;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogNoSuccessfulPartialResults"/>.
    /// </summary>
    public const int NoSuccessfulPartialResults = 5037;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogWrotePartialToAtProto"/>.
    /// </summary>
    public const int WrotePartialToAtProto = 5038;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogSuccessfullyWrotePartial"/>.
    /// </summary>
    public const int SuccessfullyWrotePartial = 5039;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFailedToWritePartial"/>.
    /// </summary>
    public const int FailedToWritePartial = 5040;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogNoExternalId"/>.
    /// </summary>
    public const int NoExternalId = 5041;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogNoOtherProviders"/>.
    /// </summary>
    public const int NoOtherProviders = 5042;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogCacheHitForExternalId"/>.
    /// </summary>
    public const int CacheHitForExternalId = 5043;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogQueuedSecondaryLookup"/>.
    /// </summary>
    public const int QueuedSecondaryLookup = 5046;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogFailedToQueueSecondary"/>.
    /// </summary>
    public const int FailedToQueueSecondary = 5047;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogAllProvidersInCache"/>.
    /// </summary>
    public const int AllProvidersInCache = 5048;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogAddedProvidersToSaga"/>.
    /// </summary>
    public const int AddedProvidersToSaga = 5049;

    /// <summary>
    /// EventId for <see cref="SagaCoordinatorBackgroundService.LogWaitingForSecondaryLookups"/>.
    /// </summary>
    public const int WaitingForSecondaryLookups = 5050;
}
