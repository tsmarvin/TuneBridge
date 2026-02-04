using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// Represents a genre resource from the Tidal API.
    /// </summary>
    /// <remarks>
    /// Genres are included in the 'included' array of JSON:API responses
    /// when tracks reference them via relationships.
    /// </remarks>
    public sealed class TidalGenre {

        /// <summary>
        /// The unique identifier for this genre resource.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The resource type. Should be "genres".
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The genre's attributes containing the genre name.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public TidalGenreAttributes? Attributes { get; set; }
    }

    /// <summary>
    /// Attributes for a Tidal genre resource.
    /// </summary>
    public sealed class TidalGenreAttributes {

        /// <summary>
        /// The display name of the genre.
        /// </summary>
        [JsonPropertyName( "genreName" )]
        public string? GenreName { get; set; }
    }

}
