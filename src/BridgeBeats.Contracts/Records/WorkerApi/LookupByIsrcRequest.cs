namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Request to lookup track information by ISRC (International Standard Recording Code).
/// </summary>
/// <param name="Isrc">The ISRC code of the track.</param>
public sealed record LookupByIsrcRequest( string Isrc );
