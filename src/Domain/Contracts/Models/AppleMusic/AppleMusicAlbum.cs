using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.AppleMusic {

    /// <summary>
    /// An album resource from the Apple Music API.
    /// Represents a complete album object with its attributes.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/albums/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/albums
    /// </remarks>
    public sealed class AppleMusicAlbum {

        /// <summary>
        /// The unique identifier for the album.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The type of the resource. Always "albums".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "albums";

        /// <summary>
        /// A relative cursor to the resource. Used for fetching additional data.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>
        /// The attributes for the album.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public AppleMusicAlbumAttributes? Attributes { get; set; }

    }

}
