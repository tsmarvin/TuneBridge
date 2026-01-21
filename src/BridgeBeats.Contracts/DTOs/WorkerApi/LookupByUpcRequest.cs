namespace BridgeBeats.Contracts.DTOs.WorkerApi;

/// <summary>
/// Request to lookup album information by UPC (Universal Product Code).
/// </summary>
/// <param name="Upc">The UPC code of the album.</param>
public sealed record LookupByUpcRequest( string Upc );
