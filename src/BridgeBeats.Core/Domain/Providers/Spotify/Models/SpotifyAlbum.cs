using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API full <c>album</c> object. Carries the
    /// fields returned when an album is fetched directly (GET /albums/{id}, GET /albums?ids=), including
    /// the full track listing, copyrights, and external ids (UPC) that the simplified variant omits. The
    /// authoritative meaning of each field is the Spotify Web API object model; <c>[JsonPropertyName]</c>
    /// attributes map each property to its wire field. See <see cref="SpotifyAlbumSimplified"/> for the
    /// simplified variant.
    /// </summary>
    public sealed class SpotifyAlbum {

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

        /// <summary>Paged listing of the album's tracks as simplified track objects; null when not returned. Maps to <c>tracks</c>.</summary>
        [JsonPropertyName( "tracks" )]
        public SpotifyPaging<SpotifyTrackSimplified>? Tracks { get; set; }

        /// <summary>Copyright statements for the album. Maps to <c>copyrights</c>.</summary>
        [JsonPropertyName( "copyrights" )]
        public List<SpotifyCopyright> Copyrights { get; set; } = [];

        /// <summary>External identifiers for the album, such as the UPC. Maps to <c>external_ids</c>.</summary>
        [JsonPropertyName( "external_ids" )]
        public SpotifyExternalIds? ExternalIds { get; set; }

        /// <summary>Genres associated with the album; empty when not yet classified. Maps to <c>genres</c>.</summary>
        [JsonPropertyName( "genres" )]
        public List<string> Genres { get; set; } = [];

        /// <summary>Record label for the album. Maps to <c>label</c>.</summary>
        [JsonPropertyName( "label" )]
        public string Label { get; set; } = string.Empty;

        /// <summary>Spotify popularity score for the album, between 0 and 100 with 100 being most popular. Maps to <c>popularity</c>.</summary>
        [JsonPropertyName( "popularity" )]
        public int Popularity { get; set; }
    }

}
