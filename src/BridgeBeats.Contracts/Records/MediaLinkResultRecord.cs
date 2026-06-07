using System.Text.Json.Serialization;
using idunno.AtProto.Repo;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// AT Protocol record for BridgeBeats MediaLinkResult.
/// Corresponds to the link.bridgebeats.lookup lexicon.
/// Note: Input links are tracked only in SQLite for privacy - not stored on PDS.
/// </summary>
public sealed record MediaLinkResultRecord : AtProtoRecord {

    /// <summary>
    /// Creates a new instance of <see cref="MediaLinkResultRecord"/>.
    /// </summary>
    public MediaLinkResultRecord( ) : base( ) { }

    /// <summary>
    /// Creates a new instance of <see cref="MediaLinkResultRecord"/>.
    /// </summary>
    /// <param name="results">Collection of lookup results from each provider.</param>
    /// <param name="lookedUpAt">ISO 8601 timestamp of when this lookup was performed.</param>
    /// <param name="isPartial">Whether this record holds partial results (some providers still pending).</param>
    [JsonConstructor]
    public MediaLinkResultRecord(
        ICollection<ProviderResultRecord> results,
        DateTimeOffset lookedUpAt,
        bool isPartial = false
    ) : base( ) {
        Results = results ?? throw new ArgumentNullException( nameof( results ) );
        LookedUpAt = lookedUpAt;
        IsPartial = isPartial;
    }

    /// <summary>
    /// Collection of lookup results from each provider that returned a match.
    /// </summary>
    [JsonPropertyName( "results" )]
    [JsonRequired]
    public ICollection<ProviderResultRecord> Results { get; init; } = [];

    /// <summary>
    /// ISO 8601 timestamp of when this lookup was performed.
    /// </summary>
    [JsonPropertyName( "lookedUpAt" )]
    [JsonRequired]
    public DateTimeOffset LookedUpAt { get; init; }

    /// <summary>
    /// Whether this record holds partial results (some providers still pending).
    /// Omitted from storage when false so existing records remain unchanged.
    /// </summary>
    [JsonPropertyName( "isPartial" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
    public bool IsPartial { get; init; }
}
