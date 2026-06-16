using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Spotify.Models {

    /// <summary>
    /// Deserialization target mirroring the Spotify Web API <c>paging object</c> — the generic wrapper
    /// around any paged collection (album tracks, an artist's albums, each facet of a search response,
    /// and so on). The authoritative meaning of each field is the Spotify Web API object model;
    /// <c>[JsonPropertyName]</c> attributes map each property to its wire field.
    /// </summary>
    /// <typeparam name="T">Element type of the paged collection, for example <see cref="SpotifyTrack"/> or <see cref="SpotifyAlbum"/>.</typeparam>
    public sealed class SpotifyPaging<T> {

        /// <summary>Spotify Web API endpoint URL that returned this page. Maps to <c>href</c>.</summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>Maximum number of items in the response, as requested for this page. Maps to <c>limit</c>.</summary>
        [JsonPropertyName( "limit" )]
        public int Limit { get; set; }

        /// <summary>URL to the next page of items, or null when this is the last page. Maps to <c>next</c>.</summary>
        [JsonPropertyName( "next" )]
        public string? Next { get; set; }

        /// <summary>Zero-based offset of the first item in this page. Maps to <c>offset</c>.</summary>
        [JsonPropertyName( "offset" )]
        public int Offset { get; set; }

        /// <summary>URL to the previous page of items, or null when this is the first page. Maps to <c>previous</c>.</summary>
        [JsonPropertyName( "previous" )]
        public string? Previous { get; set; }

        /// <summary>Total number of items available across all pages. Maps to <c>total</c>.</summary>
        [JsonPropertyName( "total" )]
        public int Total { get; set; }

        /// <summary>Items contained in this page. Maps to <c>items</c>.</summary>
        [JsonPropertyName( "items" )]
        public List<T> Items { get; set; } = [];
    }

}
