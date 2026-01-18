using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Represents the result of parsing and looking up media links for supported providers.
/// </summary>
public sealed class MediaLinkResult {

    /// <summary>
    /// The list of input media links used for the initial lookup (if applicable).
    /// </summary>
    internal readonly List<string> _inputLinks = [];

    /// <summary>
    /// The dictionary of results from supported providers with matching entries.
    /// </summary>
    public Dictionary<SupportedProviders, MusicLookupResult> Results { get; set; } = [];

    /// <summary>
    /// Optional user-facing messages associated with this result (e.g., guidance for unsupported links).
    /// </summary>
    public List<string>? Messages { get; set; }

    /// <summary>
    /// The UTC timestamp of when this lookup was performed.
    /// </summary>
    public DateTime LookedUpAt { get; init; }

    /// <inheritdoc/>
    public override bool Equals( object? obj ) {
        if (
            obj is not null &&
            obj.GetType( ) == typeof( MediaLinkResult )
        ) {
            MediaLinkResult objCast = (MediaLinkResult)obj;
            return objCast._inputLinks == _inputLinks &&
                    objCast.Results == Results;
        }
        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode( )
        => Results.GetHashCode( );
}
