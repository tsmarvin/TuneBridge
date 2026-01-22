namespace BridgeBeats.Contracts.DTOs;

/// <summary>
/// Data transfer object representing a playlist stored on a user's ATProto PDS.
/// Designed for cross-platform sharing with per-provider track substitutions.
/// </summary>
public sealed class ATProtoPlaylistDto {

    /// <summary>
    /// The AT-URI of the playlist record on the user's PDS.
    /// Set after the playlist is created/retrieved.
    /// </summary>
    public string? AtUri { get; set; }

    /// <summary>
    /// The record key (TID) for this playlist.
    /// </summary>
    public string? Rkey { get; set; }

    /// <summary>
    /// Display name of the playlist.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional description or notes for the playlist.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// DID of the user who created this playlist.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>
    /// ISO 8601 UTC timestamp of when this playlist was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// DID of the repository containing the link.bridgebeats.lookup records.
    /// Defaults to the BridgeBeats server DID.
    /// </summary>
    public string LookupRepository { get; set; } = string.Empty;

    /// <summary>
    /// Ordered list of track record keys (rkeys) referencing link.bridgebeats.lookup records.
    /// Maximum 16,384 entries.
    /// </summary>
    public IList<string> Tracks { get; set; } = [];

    /// <summary>
    /// Per-provider track substitutions.
    /// Outer key is provider name (appleMusic, spotify, tidal).
    /// Inner key is the zero-based track index (as string).
    /// Value is the substitute track rkey.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? Substitutions { get; set; }

    /// <summary>
    /// Constructs the full AT-URI for a track at the given index, applying substitutions if available.
    /// </summary>
    /// <param name="index">The zero-based track index.</param>
    /// <param name="provider">Optional provider name for substitution lookup.</param>
    /// <returns>The full AT-URI for the track.</returns>
    public string GetTrackAtUri( int index, string? provider = null ) {
        string rkey = GetTrackRkey( index, provider );
        return $"at://{LookupRepository}/link.bridgebeats.lookup/{rkey}";
    }

    /// <summary>
    /// Gets the track rkey at the given index, applying substitutions if available.
    /// </summary>
    /// <param name="index">The zero-based track index.</param>
    /// <param name="provider">Optional provider name for substitution lookup.</param>
    /// <returns>The track rkey (possibly substituted).</returns>
    public string GetTrackRkey( int index, string? provider = null ) {
        if (index < 0 || index >= Tracks.Count) {
            throw new ArgumentOutOfRangeException( nameof( index ) );
        }

        // Check for substitution
        if (!string.IsNullOrEmpty( provider ) && Substitutions is not null) {
            string providerKey = provider.ToLowerInvariant( );
            if (Substitutions.TryGetValue( providerKey, out Dictionary<string, string>? providerSubs ) &&
                providerSubs.TryGetValue( index.ToString( ), out string? substituteRkey )) {
                return substituteRkey;
            }
        }

        return Tracks[index];
    }

    /// <summary>
    /// Sets a substitution for a track at the given index for a specific provider.
    /// </summary>
    /// <param name="provider">The provider name (appleMusic, spotify, tidal).</param>
    /// <param name="index">The zero-based track index.</param>
    /// <param name="substituteRkey">The substitute track rkey.</param>
    public void SetSubstitution( string provider, int index, string substituteRkey ) {
        Substitutions ??= [];

        string providerKey = provider.ToLowerInvariant( );
        if (!Substitutions.TryGetValue( providerKey, out Dictionary<string, string>? providerSubs )) {
            providerSubs = [];
            Substitutions[providerKey] = providerSubs;
        }

        providerSubs[index.ToString( )] = substituteRkey;
    }

    /// <summary>
    /// Removes a substitution for a track at the given index for a specific provider.
    /// </summary>
    /// <param name="provider">The provider name (appleMusic, spotify, tidal).</param>
    /// <param name="index">The zero-based track index.</param>
    /// <returns>True if a substitution was removed, false otherwise.</returns>
    public bool RemoveSubstitution( string provider, int index ) {
        if (Substitutions is null) { return false; }

        string providerKey = provider.ToLowerInvariant( );
        if (!Substitutions.TryGetValue( providerKey, out Dictionary<string, string>? providerSubs )) {
            return false;
        }

        bool removed = providerSubs.Remove( index.ToString( ) );

        // Clean up empty dictionaries
        if (providerSubs.Count == 0) {
            _ = Substitutions.Remove( providerKey );
        }

        if (Substitutions.Count == 0) {
            Substitutions = null;
        }

        return removed;
    }
}
