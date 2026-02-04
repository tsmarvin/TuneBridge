namespace BridgeBeats.Contracts.Records {

    /// <summary>Request to start ATProto OAuth login.</summary>
    /// <param name="Handle">The ATProto handle (e.g., user.bsky.social).</param>
    public record AtProtoLoginRequest( string Handle );

}
