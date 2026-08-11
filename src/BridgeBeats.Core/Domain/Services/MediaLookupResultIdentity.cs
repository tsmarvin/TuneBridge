using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Core.Domain.Services;

/// <summary>Defines the provider-result identity required for deterministic durable storage.</summary>
internal static class MediaLookupResultIdentity {
    internal static bool IsPersistable( MusicLookupResult result ) =>
        HasUsableExternalId( result )
        || !string.IsNullOrWhiteSpace( result.Title )
        || !string.IsNullOrWhiteSpace( result.Artist );

    internal static bool HasUsableExternalId( MusicLookupResult result ) =>
        !string.IsNullOrWhiteSpace( result.ExternalId )
        && result.ExternalId.Any( character => char.IsLetterOrDigit( character ) || character == '-' );

    internal static string? GetExternalKey( MusicLookupResult result ) {
        ArgumentNullException.ThrowIfNull( result );

        if (!HasUsableExternalId( result )) {
            return null;
        }

        return $"{(result.IsAlbum == true ? "album" : "track")}:{result.ExternalId!.Trim( )}";
    }
}
