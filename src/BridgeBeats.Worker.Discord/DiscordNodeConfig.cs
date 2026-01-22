namespace BridgeBeats.Worker.Discord {

    /// <summary>
    /// Represents the configuration for a Discord gateway node.
    /// This is used by the <see cref="MessageCreateGatewayHandler"/> to process messages for a specific shard by node.
    /// </summary>
    /// <param name="NodeNumber">The identifier for this node (used for sharding).</param>
    public record DiscordNodeConfig( int NodeNumber );

}
