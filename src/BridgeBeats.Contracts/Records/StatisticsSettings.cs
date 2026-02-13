namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// Configuration settings for the statistics service.
    /// </summary>
    /// <param name="PdsUri">The PDS URI to query for records.</param>
    /// <param name="UserDid">The DID of the account whose collection to query.</param>
    /// <param name="CacheDuration">How long to cache statistics before refreshing.</param>
    /// <param name="StartupDelay">How long to wait after application startup before the first computation.</param>
    public sealed record StatisticsSettings(
        Uri PdsUri,
        string UserDid,
        TimeSpan CacheDuration,
        TimeSpan StartupDelay
    );

}
