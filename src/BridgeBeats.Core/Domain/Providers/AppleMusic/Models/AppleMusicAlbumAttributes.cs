using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// Attributes of an Apple Music album resource.
    /// Contains metadata like album name, artist name, UPC, artwork, and release info.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/albums/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/albums
    /// </remarks>
    public sealed class AppleMusicAlbumAttributes {

        /// <summary>
        /// The artist's name.
        /// </summary>
        [JsonPropertyName( "artistName" )]
        public string ArtistName { get; set; } = string.Empty;

        /// <summary>
        /// The artwork for the album.
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
        /// The copyright text for the album.
        /// </summary>
        [JsonPropertyName( "copyright" )]
        public string? Copyright { get; set; }

        /// <summary>
        /// The editorial notes for the album.
        /// </summary>
        [JsonPropertyName( "editorialNotes" )]
        public AppleMusicEditorialNotes? EditorialNotes { get; set; }

        /// <summary>
        /// The genre names the album is associated with.
        /// </summary>
        [JsonPropertyName( "genreNames" )]
        public List<string> GenreNames { get; set; } = [];

        /// <summary>
        /// Indicates whether the album is a compilation album.
        /// </summary>
        [JsonPropertyName( "isCompilation" )]
        public bool IsCompilation { get; set; }

        /// <summary>
        /// Indicates whether the album contains only a single song.
        /// </summary>
        [JsonPropertyName( "isSingle" )]
        public bool IsSingle { get; set; }

        /// <summary>
        /// Indicates whether the album is complete.
        /// If false, the album is not yet complete, and additional songs will be added.
        /// </summary>
        [JsonPropertyName( "isComplete" )]
        public bool IsComplete { get; set; }

        /// <summary>
        /// The localized name of the album.
        /// </summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The release date of the album, formatted as YYYY-MM-DD.
        /// </summary>
        [JsonPropertyName( "releaseDate" )]
        public string? ReleaseDate { get; set; }

        /// <summary>
        /// The number of tracks in the album.
        /// </summary>
        [JsonPropertyName( "trackCount" )]
        public int TrackCount { get; set; }

        /// <summary>
        /// The Universal Product Code (UPC) for the album.
        /// </summary>
        [JsonPropertyName( "upc" )]
        public string? Upc { get; set; }

        /// <summary>
        /// The URL for sharing the album in Apple Music.
        /// </summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
