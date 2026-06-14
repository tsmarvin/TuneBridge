using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Caches finished cross-provider lookups and looks them up by the many keys a request can
/// arrive on (input link, ISRC, UPC, metadata, provider id, card id).
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>RedisMediaLinkCache</c>
/// (<c>Infrastructure/Cache/RedisMediaLinkCache.cs</c>); cached results are also persisted to the
/// ATProto PDS. Every <c>TryGet…</c> method returns <see langword="null"/> on a cache miss and,
/// on a hit, a tuple of the cached <see cref="MediaLinkResult"/>, the backing record AT-URI
/// (<c>at://…</c>), and an <c>isStale</c> flag. <c>isStale</c> indicates the entry was found but
/// should be refreshed; callers may serve it while triggering a background refresh.
/// </remarks>
public interface IMediaLinkCacheRepository {

    /// <summary>
    /// Looks up a cached result by one of its registered input links.
    /// </summary>
    /// <param name="inputLink">An input link previously associated with a cached result.</param>
    /// <returns>
    /// A task whose result is the cache hit tuple (result, record AT-URI, <c>isStale</c>), or
    /// <see langword="null"/> on a miss.
    /// </returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultAsync( string inputLink );

    /// <summary>
    /// Looks up a cached result by ISRC (International Standard Recording Code).
    /// </summary>
    /// <param name="isrc">The ISRC to look up.</param>
    /// <returns>
    /// A task whose result is the cache hit tuple (result, record AT-URI, <c>isStale</c>), or
    /// <see langword="null"/> on a miss.
    /// </returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByISRCAsync( string isrc );

    /// <summary>
    /// Looks up a cached result by UPC (Universal Product Code).
    /// </summary>
    /// <param name="upc">The UPC to look up.</param>
    /// <returns>
    /// A task whose result is the cache hit tuple (result, record AT-URI, <c>isStale</c>), or
    /// <see langword="null"/> on a miss.
    /// </returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByUPCAsync( string upc );

    /// <summary>
    /// Looks up a cached result by free-text title and artist.
    /// </summary>
    /// <param name="title">The title to look up.</param>
    /// <param name="artist">The artist to look up.</param>
    /// <returns>
    /// A task whose result is the cache hit tuple (result, record AT-URI, <c>isStale</c>), or
    /// <see langword="null"/> on a miss.
    /// </returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByMetadataAsync( string title, string artist );

    /// <summary>
    /// Looks up a cached result by a provider's native id.
    /// </summary>
    /// <param name="providerId">The provider-native id to look up.</param>
    /// <param name="provider">The provider that <paramref name="providerId"/> belongs to.</param>
    /// <param name="isAlbum"><see langword="true"/> when the id identifies an album; <see langword="false"/> for a track.</param>
    /// <returns>
    /// A task whose result is the cache hit tuple (result, record AT-URI, <c>isStale</c>), or
    /// <see langword="null"/> on a miss.
    /// </returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum );

    /// <summary>
    /// Looks up a cached result by its deterministic card id (the hash-based, URL-safe identifier
    /// used in <c>/card/{id}</c> endpoints).
    /// </summary>
    /// <param name="cardId">The card id to look up.</param>
    /// <returns>
    /// A task whose result is the cache hit tuple (result, record AT-URI, <c>isStale</c>), or
    /// <see langword="null"/> on a miss.
    /// </returns>
    Task<(MediaLinkResult result, string recordUri, bool isStale)?> TryGetCachedResultByCardIdAsync( string cardId );

    /// <summary>
    /// Stores or updates (upserts) a finished result, indexing it under all of its lookup keys,
    /// and returns the AT-URI of the backing record.
    /// </summary>
    /// <param name="result">The <see cref="MediaLinkResult"/> to cache.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the AT-URI (<c>at://…</c>) of the cached record.</returns>
    Task<string> CacheResultAsync( MediaLinkResult result, CancellationToken cancellationToken = default );

    /// <summary>
    /// Registers additional input-link aliases for a record that is already cached, so future
    /// lookups by those links resolve to the same record.
    /// </summary>
    /// <param name="recordUri">The AT-URI (<c>at://…</c>) of the existing cached record.</param>
    /// <param name="result">The result whose input links (see <c>MediaLinkResult.InputLinks</c>) are registered as aliases.</param>
    /// <returns>A task that completes when the aliases have been registered.</returns>
    Task AddInputLinksAsync( string recordUri, MediaLinkResult result );
}
