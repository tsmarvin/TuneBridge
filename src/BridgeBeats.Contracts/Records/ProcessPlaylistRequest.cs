namespace BridgeBeats.Contracts.Records {

    /// <summary>Request to process an Apple Music playlist, identified by its id.</summary>
    /// <param name="PlaylistId">The Apple Music playlist id to process.</param>
    public record ProcessPlaylistRequest( string PlaylistId );

}
