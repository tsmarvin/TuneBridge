using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Core.Infrastructure.Utilities;

/// <summary>
/// The single source of truth for lookup key shapes. All producers that derive a
/// <c>lookupKey</c> — and therefore a saga ID — must call this class so that keys
/// produced by different components for the same entity are always identical.
/// </summary>
/// <remarks>
/// <para>
/// Two key shapes are defined:
/// <list type="bullet">
///   <item>
///     <b>Typed ID key</b> — for <c>SongIdLookup</c> / <c>AlbumIdLookup</c>:
///     <c>{lookupType}:{provider}:{normalizedId}</c>. The ID is Trim-only; no case
///     folding at this level (saga generation hashes the full key case-insensitively).
///   </item>
///   <item>
///     <b>URL key</b> — for <c>UriLookup</c>:
///     <c>UriLookup:{HashUrl(url)}</c>. The URL is normalized by
///     <see cref="HashUtility.HashUrl"/> (strips query params, protocol, www, etc.)
///     before hashing to ensure consistent lookups regardless of superficial variations.
///   </item>
/// </list>
/// </para>
/// <para>
/// All of the following call sites consume this builder; none may hand-interpolate the
/// format string:
/// <list type="bullet">
///   <item><c>LookupOrchestrator.LookupByProviderIdAsync</c></item>
///   <item><c>LookupOrchestrator.LookupByUrlAsync</c></item>
///   <item><c>JetStreamWatcherService.EnqueueMusicLinkAsync</c></item>
///   <item><c>SpotifyBulkProcessorService.ProcessBulkResultAsync</c></item>
///   <item><c>QueueProcessorBackgroundService.ProcessMessageAsync</c></item>
/// </list>
/// </para>
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
