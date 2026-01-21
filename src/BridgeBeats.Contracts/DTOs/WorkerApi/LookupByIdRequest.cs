namespace BridgeBeats.Contracts.DTOs.WorkerApi;

/// <summary>
/// Request to lookup track or album information by provider-specific ID.
/// </summary>
/// <param name="ProviderId">The provider-specific identifier (e.g., Apple Music catalog ID, Spotify track/album ID).</param>
/// <param name="IsAlbum">True to look up an album, false to look up a track.</param>
public sealed record LookupByIdRequest( string ProviderId, bool IsAlbum );
