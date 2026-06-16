namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// Request to begin an AT Protocol (Bluesky) login flow, identifying the account by its handle.
    /// </summary>
    /// <param name="Handle">The Bluesky handle (for example, <c>alice.bsky.social</c>) of the account to authenticate.</param>
    public record AtProtoLoginRequest( string Handle );

}
