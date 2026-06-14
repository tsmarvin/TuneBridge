using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>external_ids</c> object — third-party
    /// identifiers carried on full tracks (ISRC) and full albums (UPC/EAN), not on simplified objects.
    /// Each field is null when Spotify does not supply that identifier. The authoritative meaning of each
    /// field is the Spotify Web API object model; <c>[JsonPropertyName]</c> attributes map each property
    /// to its wire field.
    /// </summary>
    public sealed class SpotifyExternalIds {

        /// <summary>International Standard Recording Code for a track, a 12-character alphanumeric code identifying a recording; null when not supplied. Maps to <c>isrc</c>.</summary>
        [JsonPropertyName( "isrc" )]
        public string? Isrc { get; set; }

        /// <summary>Universal Product Code for an album, a 12-digit barcode identifying the release; null when not supplied. Maps to <c>upc</c>.</summary>
        [JsonPropertyName( "upc" )]
        public string? Upc { get; set; }

        /// <summary>International Article Number (European barcode); null when not supplied. Maps to <c>ean</c>.</summary>
        [JsonPropertyName( "ean" )]
        public string? Ean { get; set; }
    }

}
