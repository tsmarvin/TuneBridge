using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.AppleMusic {

    /// <summary>
    /// Attributes of an Apple Music artist resource.
    /// Contains metadata like artist name, genre, and URLs.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/artists/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/artists
    /// </remarks>
    public sealed class AppleMusicArtistAttributes {

        /// <summary>
        /// The localized name of the artist.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The genre names the artist is associated with.
        /// </summary>
        [JsonPropertyName( "genreNames" )]
        public List<string> GenreNames { get; set; } = [];

        /// <summary>
        /// The URL for sharing the artist in Apple Music.
        /// </summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
