namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// Configuration for the statistics service: which PDS and user to read records from, how
    /// long to cache the computed statistics, and how long to wait before the first run.
    /// </summary>
    /// <param name="PdsUri">The PDS endpoint to read records from for statistics.</param>
    /// <param name="UserDid">The decentralized identifier (DID) of the account whose records are aggregated.</param>
    /// <param name="CacheDuration">How long computed statistics remain cached before a refresh.</param>
    /// <param name="StartupDelay">How long to wait after application startup before the first statistics run.</param>
    public sealed record StatisticsSettings(
        Uri PdsUri,
        string UserDid,
        TimeSpan CacheDuration,
        TimeSpan StartupDelay
    );

}
