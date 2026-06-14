using System.Text.Json.Serialization;

namespace BridgeBeats.Core.Domain.Providers.Tidal.Models {

    /// <summary>
    /// DTO mirroring a Tidal API genre resource object.
    /// </summary>
    /// <remarks>
    /// A genre resource is identified by its <see cref="Type"/>/<see cref="Id"/> pair
    /// and carries the genre name in <see cref="Attributes"/>. Genres appear in the
    /// <c>included</c> array of a response when a track or album references them via
    /// relationships. The shape mirrors Tidal's wire format and is a deserialization
    /// target only.
    /// </remarks>
    public sealed class TidalGenre {

        /// <summary>
        /// Gets or sets the genre identifier, mapped from the Tidal <c>id</c> member.
        /// </summary>
        [JsonPropertyName( "id" )]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the resource type, mapped from the Tidal <c>type</c> member
        /// (typically <c>"genres"</c>).
        /// </summary>
        [JsonPropertyName( "type" )]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the genre's field values, mapped from the Tidal
        /// <c>attributes</c> object. May be <see langword="null"/> when the genre is
        /// referenced only as an identifier.
        /// </summary>
        [JsonPropertyName( "attributes" )]
        public TidalGenreAttributes? Attributes { get; set; }
    }

    /// <summary>
    /// DTO mirroring the <c>attributes</c> object of a Tidal genre resource.
    /// </summary>
    public sealed class TidalGenreAttributes {

        /// <summary>
        /// Gets or sets the genre name, mapped from the Tidal <c>genreName</c> member.
        /// </summary>
        [JsonPropertyName( "genreName" )]
        public string? GenreName { get; set; }
    }

}
