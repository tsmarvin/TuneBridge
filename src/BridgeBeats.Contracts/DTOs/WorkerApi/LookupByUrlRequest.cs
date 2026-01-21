namespace BridgeBeats.Contracts.DTOs.WorkerApi;

/// <summary>
/// Request to lookup music information from a provider-specific URL.
/// </summary>
/// <param name="Url">The music provider's URL (e.g., Spotify or Apple Music link).</param>
public sealed record LookupByUrlRequest( string Url );
