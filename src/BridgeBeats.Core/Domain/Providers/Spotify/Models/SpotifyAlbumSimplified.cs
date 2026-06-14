using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API simplified <c>album</c> object — the album
    /// shape embedded in track objects, search results, and an artist's albums listing
    /// (GET /artists/{id}/albums). Compared with <see cref="SpotifyAlbum"/> it omits the track listing,
    /// copyrights, external ids (UPC), genres, label, and popularity, and adds <see cref="AlbumGroup"/>;
    /// fetch the full album via GET /albums/{id} to obtain the UPC. The authoritative meaning of each
    /// field is the Spotify Web API object model; <c>[JsonPropertyName]</c> attributes map each property
    /// to its wire field.
    /// </summary>
    public sealed class SpotifyAlbumSimplified {

        /// <summary>Album type reported by Spotify, for example <c>album</c>, <c>single</c>, or <c>compilation</c>. Maps to <c>album_type</c>.</summary>
        [JsonPropertyName( "album_type" )]
        public string AlbumType { get; set; } = string.Empty;

        /// <summary>Number of tracks in the album. Maps to <c>total_tracks</c>.</summary>
        [JsonPropertyName( "total_tracks" )]
        public int TotalTracks { get; set; }

        /// <summary>ISO 3166-1 alpha-2 market codes the album is available in; present only when no market is supplied in the request. Maps to <c>available_markets</c>.</summary>
        [JsonPropertyName( "available_markets" )]
        public List<string>? AvailableMarkets { get; set; }

        /// <summary>Known external URLs for the album, primarily the Spotify web link. Maps to <c>external_urls</c>.</summary>
        [JsonPropertyName( "external_urls" )]
        public SpotifyExternalUrls? ExternalUrls { get; set; }

        /// <summary>Spotify Web API endpoint URL providing full details for the album. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Spotify id for the album. Maps to <c>id</c>.</summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>Cover art images in various sizes, widest first. Maps to <c>images</c>.</summary>
        [JsonPropertyName( "images" )]
        public List<SpotifyImage> Images { get; set; } = [];

        /// <summary>Album name. Maps to <c>name</c>.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>Release date, expressed with the precision indicated by <see cref="ReleaseDatePrecision"/> as <c>YYYY</c>, <c>YYYY-MM</c>, or <c>YYYY-MM-DD</c>. Maps to <c>release_date</c>.</summary>
        [JsonPropertyName( "release_date" )]
        public string ReleaseDate { get; set; } = string.Empty;

        /// <summary>Precision of <see cref="ReleaseDate"/>: <c>year</c>, <c>month</c>, or <c>day</c>. Maps to <c>release_date_precision</c>.</summary>
        [JsonPropertyName( "release_date_precision" )]
        public string ReleaseDatePrecision { get; set; } = string.Empty;

        /// <summary>Content restrictions that apply to the album, when present. Maps to <c>restrictions</c>.</summary>
        [JsonPropertyName( "restrictions" )]
        public SpotifyRestrictions? Restrictions { get; set; }

        /// <summary>Object type, always <c>album</c> for this object. Maps to <c>type</c>.</summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = "album";

        /// <summary>Spotify URI for the album. Maps to <c>uri</c>.</summary>
        [JsonPropertyName( "uri" )]
        public string Uri { get; set; } = string.Empty;

        /// <summary>Artists credited on the album, as simplified artist objects. Maps to <c>artists</c>.</summary>
        [JsonPropertyName( "artists" )]
        public List<SpotifyArtistSimplified> Artists { get; set; } = [];

        /// <summary>Relationship of the album to the artist it was returned for: <c>album</c>, <c>single</c>, <c>compilation</c>, or <c>appears_on</c>; present only when the album appears in an artist's albums listing. Maps to <c>album_group</c>.</summary>
        [JsonPropertyName( "album_group" )]
        public string? AlbumGroup { get; set; }
    }

}
