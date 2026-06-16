using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the attributes of the Apple Music API <c>Artists</c> resource object. Deserialization
    /// target only; the authoritative field meanings are defined by the Apple Music API.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/artists/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/artists
    /// </remarks>
    public sealed class AppleMusicArtistAttributes {

        /// <summary>The artist name.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>The artist's genre names.</summary>
        [JsonPropertyName( "genreNames" )]
        public List<string> GenreNames { get; set; } = [];

        /// <summary>The public Apple Music URL for the artist.</summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
