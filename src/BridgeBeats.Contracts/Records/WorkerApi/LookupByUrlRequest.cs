namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Worker request asking a provider to resolve an item from a provider URL (for example a Spotify
/// or Apple Music link). Sent to a provider worker's <c>/lookup/url</c> endpoint; the worker replies
/// with a <see cref="ProviderLookupResponse"/>.
/// </summary>
/// <param name="Url">The provider URL pointing at the album or track to look up.</param>
public sealed record LookupByUrlRequest( string Url );
