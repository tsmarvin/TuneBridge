namespace BridgeBeats.Contracts.Records {

    /// <summary>Request for storing Apple Music user token.</summary>
    /// <param name="UserToken">Apple Music user token from MusicKit JS.</param>
    /// <param name="ExpiresInMs">Token expiration time in milliseconds.</param>
    public record StoreTokenRequest( string UserToken, long ExpiresInMs );

}
