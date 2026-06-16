using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API simplified <c>artist</c> object — the artist
    /// shape embedded in album and track objects and search results. Compared with
    /// <see cref="SpotifyArtist"/> it omits followers, genres, images, and popularity. The authoritative
    /// meaning of each field is the Spotify Web API object model; <c>[JsonPropertyName]</c> attributes map
    /// each property to its wire field.
    /// </summary>
    public sealed class SpotifyArtistSimplified {

        /// <summary>Known external URLs for the artist, primarily the Spotify web link. Maps to <c>external_urls</c>.</summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>Spotify Web API endpoint URL providing full details for the artist. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Spotify id for the artist. Maps to <c>id</c>.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>Artist name. Maps to <c>name</c>.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>Object type, always <c>artist</c> for this object. Maps to <c>type</c>.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "artist";

        /// <summary>Spotify URI for the artist. Maps to <c>uri</c>.</summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
