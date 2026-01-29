namespace BridgeBeats.Contracts.Records {

    /// <summary>Request for processing a playlist.</summary>
    /// <param name="PlaylistId">Apple Music playlist ID.</param>
    public record ProcessPlaylistRequest( string PlaylistId );

}
