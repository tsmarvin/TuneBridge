using System.Text.Json.Serialization;
using idunno.AtProto.Repo;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// AT Protocol record for BridgeBeats user playlists.
/// Corresponds to the link.bridgebeats.playlist lexicon.
/// </summary>
public sealed record PlaylistRecord : AtProtoRecord {

    /// <summary>
    /// Creates a new instance of <see cref="PlaylistRecord"/>.
    /// </summary>
    public PlaylistRecord( ) : base( ) { }

    /// <summary>
    /// Creates a new instance of <see cref="PlaylistRecord"/>.
    /// </summary>
    /// <param name="title">Display name of the playlist.</param>
    /// <param name="createdBy">DID of the user who created this playlist.</param>
    /// <param name="updatedAt">ISO 8601 UTC timestamp of when this playlist was last updated.</param>
    /// <param name="lookupRepository">DID of the repository containing the lookup records.</param>
    /// <param name="tracks">Ordered list of track rkeys referencing link.bridgebeats.lookup records.</param>
    /// <param name="description">Optional description or notes for the playlist.</param>
    /// <param name="substitutions">Optional per-provider track substitutions.</param>
    [JsonConstructor]
    public PlaylistRecord(
        string title,
        string createdBy,
        DateTimeOffset updatedAt,
        string lookupRepository,
        IList<string> tracks,
        string? description = null,
        PlaylistSubstitutionMap? substitutions = null
    ) : base( ) {
        Title = title ?? throw new ArgumentNullException( nameof( title ) );
        CreatedBy = createdBy ?? throw new ArgumentNullException( nameof( createdBy ) );
        UpdatedAt = updatedAt;
        LookupRepository = lookupRepository ?? throw new ArgumentNullException( nameof( lookupRepository ) );
        Tracks = tracks ?? throw new ArgumentNullException( nameof( tracks ) );
        Description = description;
        Substitutions = substitutions;
    }

    /// <summary>
    /// Display name of the playlist.
    /// </summary>
    [JsonPropertyName( "title" )]
    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Optional description or notes for the playlist.
    /// </summary>
    [JsonPropertyName( "description" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Description { get; init; }

    /// <summary>
    /// DID of the user who created this playlist.
    /// </summary>
    [JsonPropertyName( "createdBy" )]
    [JsonRequired]
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// ISO 8601 UTC timestamp of when this playlist was last updated.
    /// </summary>
    [JsonPropertyName( "updatedAt" )]
    [JsonRequired]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// DID of the repository containing the link.bridgebeats.lookup records referenced by tracks.
    /// Defaults to the BridgeBeats server DID.
    /// </summary>
    [JsonPropertyName( "lookupRepository" )]
    [JsonRequired]
    public string LookupRepository { get; init; } = string.Empty;

    /// <summary>
    /// Ordered list of track record keys (rkeys) referencing link.bridgebeats.lookup records.
    /// Albums are expanded to individual tracks. Maximum 16,384 entries.
    /// </summary>
    [JsonPropertyName( "tracks" )]
    [JsonRequired]
    public IList<string> Tracks { get; init; } = [];

    /// <summary>
    /// Optional per-provider track substitutions.
    /// When playing on a specific provider, substitute the track at the given index with an alternative.
    /// </summary>
    [JsonPropertyName( "substitutions" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public PlaylistSubstitutionMap? Substitutions { get; init; }
}

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
