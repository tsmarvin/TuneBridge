namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Request to lookup music information by title and artist metadata.
/// </summary>
/// <param name="Title">The title of the track or album.</param>
/// <param name="Artist">The artist name.</param>
public sealed record LookupByMetadataRequest( string Title, string Artist );
