using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the attributes of the Apple Music API <c>Albums</c> resource object. Deserialization
    /// target only; the authoritative field meanings are defined by the Apple Music API.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/albums/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/albums
    /// </remarks>
    public sealed class AppleMusicAlbumAttributes {

        /// <summary>The album's primary artist name.</summary>
        [JsonPropertyName( "artistName" )]
        public string ArtistName { get; set; } = string.Empty;

        /// <summary>The album artwork; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "artwork" )]
        public AppleMusicArtwork? Artwork { get; set; }

        /// <summary>The content rating (for example, <c>explicit</c> or <c>clean</c>); <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "contentRating" )]
        public string? ContentRating { get; set; }

        /// <summary>The album copyright text; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "copyright" )]
        public string? Copyright { get; set; }

        /// <summary>Editorial notes about the album; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "editorialNotes" )]
        public AppleMusicEditorialNotes? EditorialNotes { get; set; }

        /// <summary>The album's genre names.</summary>
        [JsonPropertyName( "genreNames" )]
        public List<string> GenreNames { get; set; } = [];

        /// <summary>Whether the album is a compilation of tracks by various artists.</summary>
        [JsonPropertyName( "isCompilation" )]
        public bool IsCompilation { get; set; }

        /// <summary>Whether the album is a single.</summary>
        [JsonPropertyName( "isSingle" )]
        public bool IsSingle { get; set; }

        /// <summary>Whether the album is complete (all of its tracks are available).</summary>
        [JsonPropertyName( "isComplete" )]
        public bool IsComplete { get; set; }

        /// <summary>The album title.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>The release date in <c>YYYY-MM-DD</c> form; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "releaseDate" )]
        public string? ReleaseDate { get; set; }

        /// <summary>The number of tracks on the album.</summary>
        [JsonPropertyName( "trackCount" )]
        public int TrackCount { get; set; }

        /// <summary>The album's Universal Product Code; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "upc" )]
        public string? Upc { get; set; }

        /// <summary>The public Apple Music URL for the album.</summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
