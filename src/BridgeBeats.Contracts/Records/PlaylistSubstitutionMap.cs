using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records {
    /// <summary>
    /// Map of provider names to their track substitutions.
    /// Each provider maps track indices (as string keys) to alternative track rkeys.
    /// </summary>
    public sealed record PlaylistSubstitutionMap {

        /// <summary>
        /// Creates a new instance of <see cref="PlaylistSubstitutionMap"/>.
        /// </summary>
        public PlaylistSubstitutionMap( ) { }

        /// <summary>
        /// Creates a new instance of <see cref="PlaylistSubstitutionMap"/>.
        /// </summary>
        /// <param name="appleMusic">Track substitutions for Apple Music.</param>
        /// <param name="spotify">Track substitutions for Spotify.</param>
        /// <param name="tidal">Track substitutions for Tidal.</param>
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
        /// Track substitutions for Apple Music.
        /// Keys are zero-based track indices (as strings), values are substitute track rkeys.
        /// </summary>
        [JsonPropertyName( "appleMusic" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public Dictionary<string, string>? AppleMusic { get; init; }

        /// <summary>
        /// Track substitutions for Spotify.
        /// Keys are zero-based track indices (as strings), values are substitute track rkeys.
        /// </summary>
        [JsonPropertyName( "spotify" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public Dictionary<string, string>? Spotify { get; init; }

        /// <summary>
        /// Track substitutions for Tidal.
        /// Keys are zero-based track indices (as strings), values are substitute track rkeys.
        /// </summary>
        [JsonPropertyName( "tidal" )]
        [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
        public Dictionary<string, string>? Tidal { get; init; }

        /// <summary>
        /// Gets the substitution dictionary for a specific provider.
        /// </summary>
        /// <param name="provider">The provider name (appleMusic, spotify, tidal).</param>
        /// <returns>The substitution dictionary, or null if no substitutions exist for that provider.</returns>
        public Dictionary<string, string>? GetSubstitutionsForProvider( string provider ) {
            return provider.ToLowerInvariant( ) switch {
                "applemusic" => AppleMusic,
                "spotify" => Spotify,
                "tidal" => Tidal,
                _ => null
            };
        }

        /// <summary>
        /// Checks if there are any substitutions defined for any provider.
        /// </summary>
        public bool HasAnySubstitutions =>
            (AppleMusic?.Count > 0) || (Spotify?.Count > 0) || (Tidal?.Count > 0);
    }

}
