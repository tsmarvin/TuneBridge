using System.Text.Json.Serialization;
using idunno.AtProto.Repo;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// A PDS-persisted user playlist, stored as an AT Protocol record corresponding to the
/// <c>link.bridgebeats.playlist</c> lexicon. Holds playlist metadata plus the track references
/// and any per-provider track substitutions.
/// </summary>
/// <remarks>
/// <see cref="Tracks"/> holds references (record keys) into the <see cref="LookupRepository"/>
/// rather than inline track data.
/// </remarks>
public sealed record PlaylistRecord : AtProtoRecord {

    /// <summary>
    /// Initializes an empty playlist record. Present for the AT Protocol deserializer; populate
    /// the properties via <c>init</c> after construction.
    /// </summary>
    public PlaylistRecord( ) : base( ) { }

    /// <summary>
    /// Initializes a playlist record with its metadata, track references, and optional fields.
    /// Used by JSON deserialization.
    /// </summary>
    /// <param name="title">The display name of the playlist.</param>
    /// <param name="createdBy">The DID of the user who created this playlist.</param>
    /// <param name="updatedAt">The ISO 8601 UTC timestamp of when the playlist was last updated.</param>
    /// <param name="lookupRepository">The DID of the repository containing the lookup records the track references point into.</param>
    /// <param name="tracks">The ordered track record keys (rkeys) referencing <c>link.bridgebeats.lookup</c> records.</param>
    /// <param name="description">An optional description or notes for the playlist.</param>
    /// <param name="substitutions">Optional per-provider track substitutions; see <see cref="PlaylistSubstitutionMap"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="title"/>, <paramref name="createdBy"/>, <paramref name="lookupRepository"/>, or <paramref name="tracks"/> is <see langword="null"/>.</exception>
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

    /// <summary>The display name of the playlist.</summary>
    [JsonPropertyName( "title" )]
    [JsonRequired]
    public string Title { get; init; } = string.Empty;

    /// <summary>An optional description or notes for the playlist.</summary>
    [JsonPropertyName( "description" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public string? Description { get; init; }

    /// <summary>The DID of the user who created this playlist.</summary>
    [JsonPropertyName( "createdBy" )]
    [JsonRequired]
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>The ISO 8601 UTC timestamp of when this playlist was last updated.</summary>
    [JsonPropertyName( "updatedAt" )]
    [JsonRequired]
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// The DID of the repository containing the <c>link.bridgebeats.lookup</c> records referenced
    /// by <see cref="Tracks"/>. Defaults to the BridgeBeats server DID.
    /// </summary>
    [JsonPropertyName( "lookupRepository" )]
    [JsonRequired]
    public string LookupRepository { get; init; } = string.Empty;

    /// <summary>
    /// The ordered list of track record keys (rkeys) referencing <c>link.bridgebeats.lookup</c>
    /// records, resolved against <see cref="LookupRepository"/> rather than holding inline track
    /// data. Albums are expanded to individual tracks. Maximum 16,384 entries.
    /// </summary>
    [JsonPropertyName( "tracks" )]
    [JsonRequired]
    public IList<string> Tracks { get; init; } = [];

    /// <summary>
    /// Optional per-provider track substitutions. When playing on a specific provider, substitute
    /// the track at the given index with an alternative; see <see cref="PlaylistSubstitutionMap"/>.
    /// </summary>
    [JsonPropertyName( "substitutions" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
    public PlaylistSubstitutionMap? Substitutions { get; init; }
}
