using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// A song resource from the Apple Music API.
    /// Represents a complete song object with its attributes.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/songs/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/songs
    /// </remarks>
    public sealed class AppleMusicSong {

        /// <summary>
        /// The unique identifier for the song.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The type of the resource. Always "songs".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "songs";

        /// <summary>
        /// A relative cursor to the resource. Used for fetching additional data.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string? Href { get; set; }

        /// <summary>
        /// The attributes for the song.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public AppleMusicSongAttributes? Attributes { get; set; }

    }

}
