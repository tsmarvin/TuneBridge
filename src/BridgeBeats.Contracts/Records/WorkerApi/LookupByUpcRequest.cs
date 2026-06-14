namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Worker request asking a provider to resolve an album or release by its UPC (Universal Product
/// Code / barcode). Sent to a provider worker's <c>/lookup/upc</c> endpoint; the worker replies with
/// a <see cref="ProviderLookupResponse"/>.
/// </summary>
/// <param name="Upc">The UPC barcode identifying the album or release to look up.</param>
public sealed record LookupByUpcRequest( string Upc );
