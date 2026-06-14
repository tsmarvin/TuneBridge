namespace BridgeBeats.Worker.CacheBootstrap;

/// <summary>
/// Tuning settings for <see cref="CacheBootstrapBackgroundService"/>: which ATProto PDS and user to
/// read from, and how often to re-run the cache-rebuild pass.
/// </summary>
/// <param name="PdsUri">The base URI of the ATProto personal data server whose records are streamed.</param>
/// <param name="UserDid">The decentralized identifier of the user whose records are rebuilt into the cache.</param>
/// <param name="BootstrapInterval">The interval between bootstrap runs after the initial startup run.</param>
public sealed record CacheBootstrapSettings(
    Uri PdsUri,
    string UserDid,
    TimeSpan BootstrapInterval
);
