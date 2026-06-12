using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Core.Infrastructure.Utilities;

/// <summary>
/// The single source of truth for lookup key shapes. All producers that derive a
/// <c>lookupKey</c> — and therefore a saga ID — must call this class so that keys
/// produced by different components for the same entity are always identical.
/// </summary>
/// <remarks>
/// Typed-ID shape: <c>{lookupType}:{provider}:{normalizedId}</c> (Trim-only; saga hashes case-insensitively).
/// URL shape: <c>UriLookup:{HashUrl(url)}</c> (normalized via <see cref="HashUtility.HashUrl"/>).
/// All producers must use this builder; no hand-interpolated format strings.
/// </remarks>
public static class LookupKeyBuilder {

    /// <summary>
    /// Builds the lookup key for a typed provider-ID request
    /// (<c>SongIdLookup</c> or <c>AlbumIdLookup</c>).
    /// </summary>
    /// <param name="lookupType">
    /// <see cref="LookupRequestType.SongIdLookup"/> or
    /// <see cref="LookupRequestType.AlbumIdLookup"/>.
    /// </param>
    /// <param name="provider">The music provider owning this ID.</param>
    /// <param name="normalizedId">The provider-specific entity ID (Trim-only normalization).</param>
    /// <returns>A lookup key in the format <c>{lookupType}:{provider}:{normalizedId}</c>.</returns>
    public static string TypedKey( LookupRequestType lookupType, SupportedProviders provider, string normalizedId ) {
        return $"{lookupType}:{provider}:{normalizedId}";
    }

    /// <summary>
    /// Builds the lookup key for a URL-based lookup (<c>UriLookup</c>).
    /// The URL is normalized and hashed so that superficial URL variations
    /// (trailing slash, query params, www prefix) resolve to the same key.
    /// </summary>
    /// <param name="url">The raw URL to hash.</param>
    /// <returns>A lookup key in the format <c>UriLookup:{hash}</c>.</returns>
    public static string UrlKey( string url ) {
        return $"{LookupRequestType.UriLookup}:{HashUtility.HashUrl( url )}";
    }
}
