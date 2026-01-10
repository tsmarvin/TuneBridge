using System.Text.Json.Serialization;

namespace BridgeBeats.Domain.Contracts.Models.AppleMusic {

    /// <summary>
    /// Standard Apple Music API response wrapper containing a data array.
    /// Used for endpoints that return a collection of resources.
    /// </summary>
    /// <typeparam name="T">The type of resource in the data array (e.g., AppleMusicSong, AppleMusicAlbum, AppleMusicArtist).</typeparam>
    /// <remarks>
    /// Most Apple Music API endpoints return data in this format:
    /// {
    ///   "data": [
    ///     { "id": "...", "type": "...", "attributes": {...} }
    ///   ]
    /// }
    /// </remarks>
    public sealed class AppleMusicDataResponse<T> {

        /// <summary>
        /// An array of resources returned by the API.
        /// </summary>
        [JsonPropertyName( "data" )]
        public List<T> Data { get; set; } = [];

        /// <summary>
        /// A relative cursor to fetch the next page of resources, if available.
        /// </summary>
        [JsonPropertyName( "next" )]
        public string? Next { get; set; }

    }

}
