using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.Spotify {

    /// <summary>
    /// A paginated response wrapper used by Spotify's list endpoints.
    /// </summary>
    /// <typeparam name="T">The type of items in the page.</typeparam>
    /// <remarks>
    /// Documentation: https://developer.spotify.com/documentation/web-api
    /// </remarks>
    public sealed class SpotifyPaging<T> {

        /// <summary>
        /// A link to the Web API endpoint returning the full result of the request.
        /// </summary>
        [JsonPropertyName( "href" )]
        public string Href { get; set; } = string.Empty;

        /// <summary>
        /// The maximum number of items in the response.
        /// </summary>
        [JsonPropertyName( "limit" )]
        public int Limit { get; set; }

        /// <summary>
        /// URL to the next page of items (null if none).
        /// </summary>
        [JsonPropertyName( "next" )]
        public string? Next { get; set; }

        /// <summary>
        /// The offset of the items returned.
        /// </summary>
        [JsonPropertyName( "offset" )]
        public int Offset { get; set; }

        /// <summary>
        /// URL to the previous page of items (null if none).
        /// </summary>
        [JsonPropertyName( "previous" )]
        public string? Previous { get; set; }

        /// <summary>
        /// The total number of items available to return.
        /// </summary>
        [JsonPropertyName( "total" )]
        public int Total { get; set; }

        /// <summary>
        /// The requested content.
        /// </summary>
        [JsonPropertyName( "items" )]
        public List<T> Items { get; set; } = [];
    }

}
