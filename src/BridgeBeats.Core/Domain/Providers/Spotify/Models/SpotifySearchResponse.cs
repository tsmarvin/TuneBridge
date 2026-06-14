using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>search</c> response (GET /search). Each
    /// requested item type is returned as its own paged facet; a facet is null when that type was not
    /// requested or had no results. The authoritative meaning of each field is the Spotify Web API object
    /// model; <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    /// <remarks>
    /// The query supports field filters such as <c>track:</c>, <c>artist:</c>, <c>album:</c>,
    /// <c>year:</c>, <c>genre:</c>, <c>isrc:</c> (tracks only), and <c>upc:</c> (albums only). For
    /// example, <c>isrc:USRC12345678</c> returns the track with that ISRC and
    /// <c>track:Alive artist:Daft Punk</c> returns tracks matching both filters.
    /// </remarks>
    public sealed class SpotifySearchResponse {

        /// <summary>Paged track results as full track objects; present only when <c>track</c> was in the type parameter. Maps to <c>tracks</c>.</summary>
        [JsonPropertyName( "tracks" )]
        public SpotifyPaging<SpotifyTrack>? Tracks { get; set; }

        /// <summary>Paged album results as full album objects; present only when <c>album</c> was in the type parameter. Maps to <c>albums</c>.</summary>
        [JsonPropertyName( "albums" )]
        public SpotifyPaging<SpotifyAlbum>? Albums { get; set; }

        /// <summary>Paged artist results as full artist objects; present only when <c>artist</c> was in the type parameter. Maps to <c>artists</c>.</summary>
        [JsonPropertyName( "artists" )]
        public SpotifyPaging<SpotifyArtist>? Artists { get; set; }

        /// <summary>Paged playlist results as simplified playlist objects; present only when <c>playlist</c> was in the type parameter. Maps to <c>playlists</c>.</summary>
        [JsonPropertyName( "playlists" )]
        public SpotifyPaging<SpotifyPlaylistSimplified>? Playlists { get; set; }

        /// <summary>Paged show results as simplified show objects; present only when <c>show</c> was in the type parameter. Maps to <c>shows</c>.</summary>
        [JsonPropertyName( "shows" )]
        public SpotifyPaging<SpotifyShowSimplified>? Shows { get; set; }

        /// <summary>Paged episode results as simplified episode objects; present only when <c>episode</c> was in the type parameter. Maps to <c>episodes</c>.</summary>
        [JsonPropertyName( "episodes" )]
        public SpotifyPaging<SpotifyEpisodeSimplified>? Episodes { get; set; }

        /// <summary>Paged audiobook results as simplified audiobook objects; present only when <c>audiobook</c> was in the type parameter. Maps to <c>audiobooks</c>.</summary>
        [JsonPropertyName( "audiobooks" )]
        public SpotifyPaging<SpotifyAudiobookSimplified>? Audiobooks { get; set; }
    }

}
