using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API full <c>artist</c> object. Carries the
    /// fields returned when an artist is fetched directly (GET /artists/{id}) or as a search result,
    /// including followers, genres, images, and popularity that the simplified variant omits. The
    /// authoritative meaning of each field is the Spotify Web API object model; <c>[JsonPropertyName]</c>
    /// attributes map each property to its wire field. See <see cref="SpotifyArtistSimplified"/> for the
    /// simplified variant.
    /// </summary>
    public sealed class SpotifyArtist {

        /// <summary>Known external URLs for the artist, primarily the Spotify web link. Maps to <c>external_urls</c>.</summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>Follower information for the artist. Maps to <c>followers</c>.</summary>
        [JsonPropertyName( "followers" )]
        public SpotifyFollowers? Followers { get; set; }

        /// <summary>Genres associated with the artist; empty when not yet classified. Maps to <c>genres</c>.</summary>
        [JsonPropertyName( "genres" )]
        public List<string> Genres { get; set; } = [];

        /// <summary>Spotify Web API endpoint URL providing full details for the artist. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Spotify id for the artist. Maps to <c>id</c>.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>Artist images in various sizes, widest first. Maps to <c>images</c>.</summary>
        [JsonPropertyName( "images" )]
        public List<SpotifyImage> Images { get; set; } = [];

        /// <summary>Artist name. Maps to <c>name</c>.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>Spotify popularity score for the artist, between 0 and 100 with 100 being most popular, calculated from the popularity of all the artist's tracks. Maps to <c>popularity</c>.</summary>
        [JsonPropertyName( "popularity" )]
        public int Popularity { get; set; }

        /// <summary>Object type, always <c>artist</c> for this object. Maps to <c>type</c>.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "artist";

        /// <summary>Spotify URI for the artist. Maps to <c>uri</c>.</summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;
    }

}
