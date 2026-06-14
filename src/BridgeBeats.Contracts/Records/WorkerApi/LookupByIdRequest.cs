namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Worker request asking a provider to resolve an item by its provider-native identifier.
/// Sent to a provider worker's <c>/lookup/id</c> endpoint; the worker replies with a
/// <see cref="ProviderLookupResponse"/>.
/// </summary>
/// <param name="ProviderId">
/// The provider's own identifier for the album or track to look up (for example an Apple Music
/// catalog id or a Spotify track/album id).
/// </param>
/// <param name="IsAlbum">
/// <see langword="true"/> to resolve <paramref name="ProviderId"/> as an album; otherwise it is
/// resolved as a track.
/// </param>
public sealed record LookupByIdRequest( string ProviderId, bool IsAlbum );
