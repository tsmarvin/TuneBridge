namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// Request to store an Apple Music user token with a relative time-to-live.
    /// </summary>
    /// <param name="UserToken">The Apple Music user token from MusicKit JS.</param>
    /// <param name="ExpiresInMs">The token's time-to-live in milliseconds, relative to when it is stored.</param>
    public record StoreTokenRequest( string UserToken, long ExpiresInMs );

}
