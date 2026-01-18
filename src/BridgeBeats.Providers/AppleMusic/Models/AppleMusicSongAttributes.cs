using System.Text.Json.Serialization;

namespace BridgeBeats.Providers.AppleMusic.Models {

    /// <summary>
    /// Attributes of an Apple Music song resource.
    /// Contains metadata like track name, artist name, ISRC, artwork, and playback info.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/songs/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/songs
    /// </remarks>
    public sealed class AppleMusicSongAttributes {

        /// <summary>
        /// The name of the album the song appears on.
        /// </summary>
        [JsonPropertyName( "albumName" )]
        public string AlbumName { get; set; } = string.Empty;

        /// <summary>
        /// The artist's name.
        /// </summary>
        [JsonPropertyName( "artistName" )]
        public string ArtistName { get; set; } = string.Empty;

        /// <summary>
        /// The artwork for the song's album.
        /// </summary>
        [JsonPropertyName( "artwork" )]
        public AppleMusicArtwork? Artwork { get; set; }

        /// <summary>
        /// The Recording Industry Association of America (RIAA) rating for the content.
        /// Possible values: clean, explicit.
        /// </summary>
        [JsonPropertyName( "contentRating" )]
        public string? ContentRating { get; set; }

        /// <summary>
        /// The disc number the song appears on.
        /// </summary>
        [JsonPropertyName( "discNumber" )]
        public int? DiscNumber { get; set; }

        /// <summary>
        /// The duration of the song in milliseconds.
        /// </summary>
        [JsonPropertyName( "durationInMillis" )]
        public long? DurationInMillis { get; set; }

        /// <summary>
        /// The genre names the song is associated with.
        /// </summary>
        [JsonPropertyName( "genreNames" )]
        public List<string> GenreNames { get; set; } = [];

        /// <summary>
        /// Indicates whether the song has lyrics available in Apple Music.
        /// </summary>
        [JsonPropertyName( "hasLyrics" )]
        public bool HasLyrics { get; set; }

        /// <summary>
        /// The International Standard Recording Code (ISRC) for the song.
        /// </summary>
        [JsonPropertyName( "isrc" )]
        public string? Isrc { get; set; }

        /// <summary>
        /// The localized name of the song.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The parameters to use to preview the song.
        /// </summary>
        [JsonPropertyName( "previews" )]
        public List<AppleMusicPreview>? Previews { get; set; }

        /// <summary>
        /// The release date of the song, formatted as YYYY-MM-DD.
        /// </summary>
        [JsonPropertyName( "releaseDate" )]
        public string? ReleaseDate { get; set; }

        /// <summary>
        /// The number of the song in the album's track list.
        /// </summary>
        [JsonPropertyName( "trackNumber" )]
        public int? TrackNumber { get; set; }

        /// <summary>
        /// The URL for sharing the song in Apple Music.
        /// </summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
