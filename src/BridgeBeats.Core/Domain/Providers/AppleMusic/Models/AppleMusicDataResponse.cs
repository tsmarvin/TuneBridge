using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic.Models {

    /// <summary>
    /// DTO mirroring the Apple Music API <c>data</c>-wrapped response envelope. Deserialization target only.
    /// Used for endpoints that return a collection of resources.
    /// </summary>
    /// <typeparam name="T">
    /// The resource type carried in the <see cref="Data"/> collection (for example, AppleMusicSong,
    /// AppleMusicAlbum, or AppleMusicArtist).
    /// </typeparam>
    /// <remarks>
    /// Most Apple Music API endpoints return data in this format:
    /// {
    ///   "data": [
    ///     { "id": "...", "type": "...", "attributes": {...} }
    ///   ]
    /// }
    /// </remarks>
    public sealed class AppleMusicDataResponse<T> {

        /// <summary>The returned resources.</summary>
        [JsonPropertyName( "data" )]
        public List<T> Data { get; set; } = [];

        /// <summary>Relative path to the next page of results; <see langword="null"/> when there are no further pages.</summary>
        [JsonPropertyName( "next" )]
        public string? Next { get; set; }

    }

}
