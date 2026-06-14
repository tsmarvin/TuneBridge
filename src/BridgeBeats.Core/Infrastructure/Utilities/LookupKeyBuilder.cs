using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Core.Infrastructure.Utilities;

/// <summary>
/// The single source of truth for the string keys used to index lookups in Redis and the
/// media-link cache. The exact key format is load-bearing: it is used directly as a Redis/cache
/// key (and therefore as a saga ID), so every producer on both the write and read paths must call
/// this class to guarantee identical strings for the same logical lookup.
/// </summary>
/// <remarks>
/// Typed-ID shape: <c>{lookupType}:{provider}:{normalizedId}</c> (Trim-only normalization; the saga
/// hashes case-insensitively). URL shape: <c>UriLookup:{HashUrl(url)}</c>. All producers must use
/// this builder; no hand-interpolated format strings.
/// </remarks>
public static class LookupKeyBuilder {

    /// <summary>
    /// Builds the lookup key for a typed, provider-scoped identifier
    /// (<see cref="LookupRequestType.SongIdLookup"/> or <see cref="LookupRequestType.AlbumIdLookup"/>).
    /// </summary>
    /// <param name="lookupType">The kind of lookup the key represents.</param>
    /// <param name="provider">The music provider the identifier belongs to.</param>
    /// <param name="normalizedId">The already-normalized provider-specific entity ID (Trim-only normalization).</param>
    /// <returns>
    /// A key of the form <c>{lookupType}:{provider}:{normalizedId}</c>, where the enum values are
    /// rendered by their default string form.
    /// </returns>
    public static string TypedKey( LookupRequestType lookupType, SupportedProviders provider, string normalizedId ) {
        return $"{lookupType}:{provider}:{normalizedId}";
    }

    /// <summary>
    /// Builds the lookup key for a URL-based lookup by hashing its normalized form, so that
    /// superficial URL variations (trailing slash, query params, www prefix) resolve to the same key.
    /// </summary>
    /// <param name="url">The raw URL to hash.</param>
    /// <returns>
    /// A key of the form <c>{UriLookup}:{hash}</c>, where <c>UriLookup</c> is the string form of
    /// <see cref="BridgeBeats.Contracts.Enums.LookupRequestType.UriLookup"/> and <c>hash</c> is the
    /// lowercase base32 SHA-256 of the normalized URL produced by
    /// <see cref="HashUtility.HashUrl(string)"/>.
    /// </returns>
    public static string UrlKey( string url ) {
        return $"{LookupRequestType.UriLookup}:{HashUtility.HashUrl( url )}";
    }
}
