namespace BridgeBeats.Contracts.DTOs.WorkerApi;

/// <summary>
/// Request to lookup additional information from a partial music lookup result.
/// Used for cross-platform matching when we have metadata from another provider.
/// </summary>
/// <param name="Artist">The primary artist name.</param>
/// <param name="Title">The track or album title.</param>
/// <param name="ExternalId">The ISRC (tracks) or UPC (albums) if available.</param>
/// <param name="IsAlbum">True if this is an album, false if it's a track.</param>
public sealed record LookupFromResultRequest(
    string Artist,
    string Title,
    string? ExternalId,
    bool? IsAlbum
);
