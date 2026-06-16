using System.Text.Json.Serialization;
using idunno.AtProto.Repo;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// The PDS-persisted form of a finished cross-provider lookup, stored as an AT Protocol
/// record corresponding to the <c>link.bridgebeats.lookup</c> lexicon. Holds the per-provider
/// result rows and when the lookup ran. This is the at-rest twin of the in-memory aggregate;
/// see the corresponding <c>MediaLinkResult</c> DTO. Input links are tracked only in SQLite
/// for privacy and are not stored on the PDS.
/// </summary>
public sealed record MediaLinkResultRecord : AtProtoRecord {

    /// <summary>
    /// Initializes an empty record. Present for the AT Protocol deserializer; populate the
    /// properties via <c>init</c> after construction.
    /// </summary>
    public MediaLinkResultRecord( ) : base( ) { }

    /// <summary>
    /// Initializes a record with its result rows and lookup time. Used by JSON deserialization.
    /// </summary>
    /// <param name="results">The per-provider result rows that make up this lookup.</param>
    /// <param name="lookedUpAt">The absolute instant the lookup was performed.</param>
    /// <param name="isPartial"><see langword="true"/> when not every provider contributed a result; defaults to <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is <see langword="null"/>.</exception>
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

    /// <summary>The per-provider result rows captured for this lookup; see <see cref="ProviderResultRecord"/>.</summary>
    [JsonPropertyName( "results" )]
    [JsonRequired]
    public ICollection<ProviderResultRecord> Results { get; init; } = [];

    /// <summary>The ISO 8601 timestamp (absolute instant) at which the lookup was performed.</summary>
    [JsonPropertyName( "lookedUpAt" )]
    [JsonRequired]
    public DateTimeOffset LookedUpAt { get; init; }

    /// <summary>
    /// <see langword="true"/> when the persisted result is partial (not every provider responded).
    /// Omitted from JSON when <see langword="false"/> so existing records remain unchanged.
    /// </summary>
    [JsonPropertyName( "isPartial" )]
    [JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
    public bool IsPartial { get; init; }
}
