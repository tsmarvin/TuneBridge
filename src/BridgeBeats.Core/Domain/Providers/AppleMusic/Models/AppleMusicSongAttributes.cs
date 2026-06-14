using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the attributes of the Apple Music API <c>Songs</c> resource object. Deserialization
    /// target only; the authoritative field meanings are defined by the Apple Music API.
    /// </summary>
    /// <remarks>
    /// Endpoint: GET /v1/catalog/{storefront}/songs/{id}
    /// Documentation: https://developer.apple.com/documentation/applemusicapi/songs
    /// </remarks>
    public sealed class AppleMusicSongAttributes {

        /// <summary>The name of the album the song belongs to.</summary>
        [JsonPropertyName( "albumName" )]
        public string AlbumName { get; set; } = string.Empty;

        /// <summary>The song's primary artist name.</summary>
        [JsonPropertyName( "artistName" )]
        public string ArtistName { get; set; } = string.Empty;

        /// <summary>The artwork for the song's album; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "artwork" )]
        public AppleMusicArtwork? Artwork { get; set; }

        /// <summary>The content rating (for example, <c>explicit</c> or <c>clean</c>); <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "contentRating" )]
        public string? ContentRating { get; set; }

        /// <summary>The disc number the song appears on; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "discNumber" )]
        public int? DiscNumber { get; set; }

        /// <summary>The song duration in milliseconds; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "durationInMillis" )]
        public long? DurationInMillis { get; set; }

        /// <summary>The song's genre names.</summary>
        [JsonPropertyName( "genreNames" )]
        public List<string> GenreNames { get; set; } = [];

        /// <summary>Whether lyrics are available for the song in Apple Music.</summary>
        [JsonPropertyName( "hasLyrics" )]
        public bool HasLyrics { get; set; }

        /// <summary>The song's International Standard Recording Code; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "isrc" )]
        public string? Isrc { get; set; }

        /// <summary>The song title.</summary>
        [JsonPropertyName( "name" )]
        public string Name { get; set; } = string.Empty;

        /// <summary>Audio preview clips for the song; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "previews" )]
        public List<AppleMusicPreview>? Previews { get; set; }

        /// <summary>The release date in <c>YYYY-MM-DD</c> form; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "releaseDate" )]
        public string? ReleaseDate { get; set; }

        /// <summary>The number of the song in the album's track list; <see langword="null"/> when not supplied.</summary>
        [JsonPropertyName( "trackNumber" )]
        public int? TrackNumber { get; set; }

        /// <summary>The public Apple Music URL for the song.</summary>
        [JsonPropertyName( "url" )]
        public string Url { get; set; } = string.Empty;

    }

}
