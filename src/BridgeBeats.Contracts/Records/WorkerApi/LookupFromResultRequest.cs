namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Worker request asking a provider to find its own equivalent of an item that another provider
/// has already resolved. This drives cross-provider matching: given an anchor result from one
/// provider, the target worker searches for the matching album or track in its own catalog.
/// Sent to a provider worker's <c>/lookup/from-result</c> endpoint; the worker replies with a
/// <see cref="ProviderLookupResponse"/>.
/// </summary>
/// <param name="Artist">The artist name from the already-resolved result, used to match.</param>
/// <param name="Title">The title from the already-resolved result, used to match.</param>
/// <param name="ExternalId">
/// Optional identifier carried over from the source result — the ISRC for tracks or the UPC for
/// albums. Acts as a matching hint when the target provider can use it; <see langword="null"/> when
/// no such id is available.
/// </param>
/// <param name="IsAlbum">
/// Optional hint indicating whether the item is an album (<see langword="true"/>) or a track
/// (<see langword="false"/>); <see langword="null"/> when the caller leaves it unspecified.
/// </param>
public sealed record LookupFromResultRequest(
    string Artist,
    string Title,
    string? ExternalId,
    bool? IsAlbum
);
