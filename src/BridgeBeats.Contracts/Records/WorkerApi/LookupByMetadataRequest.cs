namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Worker request asking a provider to resolve an item by free-text title and artist.
/// Used when no exact identifier (id, ISRC, UPC, URL) is available, so the worker performs a
/// text search. Sent to a provider worker's <c>/lookup/metadata</c> endpoint; the worker replies
/// with a <see cref="ProviderLookupResponse"/>.
/// </summary>
/// <param name="Title">The track or album title to search for.</param>
/// <param name="Artist">The artist name to search for.</param>
public sealed record LookupByMetadataRequest( string Title, string Artist );
