namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Worker request asking a provider to resolve a track by its ISRC (International Standard
/// Recording Code). Sent to a provider worker's <c>/lookup/isrc</c> endpoint; the worker replies
/// with a <see cref="ProviderLookupResponse"/>.
/// </summary>
/// <param name="Isrc">The ISRC identifying the recording (track) to look up.</param>
public sealed record LookupByIsrcRequest( string Isrc );
