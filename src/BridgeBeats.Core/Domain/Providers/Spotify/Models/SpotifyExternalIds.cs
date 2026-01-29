using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// External IDs for a Spotify resource (ISRC, UPC, EAN).
    /// Only available on full track/album objects, not simplified versions.
    /// </summary>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyExternalIds {

        /// <summary>
        /// International Standard Recording Code (for tracks).
        /// 12-character alphanumeric code uniquely identifying a recording.
        /// </summary>
        [JsonPropertyName( "isrc" )]
        public string? Isrc { get; set; }

        /// <summary>
        /// Universal Product Code (for albums).
        /// 12-digit barcode identifying the album release.
        /// </summary>
        [JsonPropertyName( "upc" )]
        public string? Upc { get; set; }

        /// <summary>
        /// International Article Number (European barcode).
        /// </summary>
        [JsonPropertyName( "ean" )]
        public string? Ean { get; set; }
    }

}
