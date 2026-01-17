using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.AppleMusic.Models {

    /// <summary>
    /// An artist resource from the Apple Music API.
    /// Represents a complete artist object with its attributes.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/artists/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/artists
    /// </remarks>
    public sealed class AppleMusicArtist {

        /// <summary>
        /// The unique identifier for the artist.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The type of the resource. Always "artists".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "artists";

        /// <summary>
        /// A relative cursor to the resource. Used for fetching additional data.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>
        /// The attributes for the artist.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public AppleMusicArtistAttributes? Attributes { get; set; }

    }

}
