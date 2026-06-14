using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records {
    /// <summary>
    /// Map of provider names to their track substitutions. Each provider maps track indices
    /// (as string keys) to alternative track rkeys.
    /// </summary>
    public sealed record PlaylistSubstitutionMap {

        /// <summary>Initializes an empty substitution map with no per-provider overrides.</summary>
        public PlaylistSubstitutionMap( ) { }

        /// <summary>
        /// Initializes a substitution map with the given per-provider override dictionaries.
        /// Used by JSON deserialization.
        /// </summary>
        /// <param name="appleMusic">Track substitutions for Apple Music, or <see langword="null"/> when none.</param>
        /// <param name="spotify">Track substitutions for Spotify, or <see langword="null"/> when none.</param>
        /// <param name="tidal">Track substitutions for Tidal, or <see langword="null"/> when none.</param>
        [JsonConstructor]
        public PlaylistSubstitutionMap(
            Dictionary<string, string>? appleMusic = null,
            Dictionary<string, string>? spotify = null,
            Dictionary<string, string>? tidal = null
        ) {
            AppleMusic = appleMusic;
            Spotify = spotify;
            Tidal = tidal;
        }

        /// <summary>
        /// Track substitutions for Apple Music. Keys are zero-based track indices (as strings),
        /// values are substitute track rkeys; <see langword="null"/> when none.
        /// </summary>
        [JsonPropertyName( "appleMusic" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public Dictionary<string, string>? AppleMusic { get; init; }

        /// <summary>
        /// Track substitutions for Spotify. Keys are zero-based track indices (as strings),
        /// values are substitute track rkeys; <see langword="null"/> when none.
        /// </summary>
        [JsonPropertyName( "spotify" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public Dictionary<string, string>? Spotify { get; init; }

        /// <summary>
        /// Track substitutions for Tidal. Keys are zero-based track indices (as strings),
        /// values are substitute track rkeys; <see langword="null"/> when none.
        /// </summary>
        [JsonPropertyName( "tidal" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public Dictionary<string, string>? Tidal { get; init; }

        /// <summary>
        /// Returns the substitution dictionary for the named provider, matching on the lowercased
        /// names <c>applemusic</c>, <c>spotify</c>, and <c>tidal</c>.
        /// </summary>
        /// <param name="provider">The provider name to resolve (case-insensitive).</param>
        /// <returns>The provider's substitution dictionary, or <see langword="null"/> when the name is unrecognized or has no overrides.</returns>
        public Dictionary<string, string>? GetSubstitutionsForProvider( string provider ) {
            return provider.ToLowerInvariant( ) switch {
                "applemusic" => AppleMusic,
                "spotify" => Spotify,
                "tidal" => Tidal,
                _ => null
            };
        }

        /// <summary><see langword="true"/> when any provider has at least one substitution defined.</summary>
        public bool HasAnySubstitutions =>
            (AppleMusic?.Count > 0) || (Spotify?.Count > 0) || (Tidal?.Count > 0);
    }

}
