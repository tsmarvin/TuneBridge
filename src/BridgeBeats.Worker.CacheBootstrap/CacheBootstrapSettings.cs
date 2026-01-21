namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Configuration settings for the cache bootstrap service.
/// </summary>
/// <param name="PdsUri">The PDS URI to query for records.</param>
/// <param name="UserDid">The DID of the account whose collection to query.</param>
/// <param name="BootstrapInterval">The interval between cache bootstrap runs.</param>
public sealed record CacheBootstrapSettings(
    Uri PdsUri,
    string UserDid,
    TimeSpan BootstrapInterval
);
