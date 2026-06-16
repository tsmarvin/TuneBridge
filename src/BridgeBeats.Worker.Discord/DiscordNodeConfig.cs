namespace BridgeBeats.Worker.Discord {

    /// <summary>
    /// Identifies which Discord gateway shard this worker process owns. The Discord bot is sharded
    /// across nodes: each running worker handles only the gateway shard whose id equals its
    /// <see cref="NodeNumber"/>, so the shard space is partitioned by running more nodes with
    /// distinct numbers. Registered as a singleton from the <c>BridgeBeats:NodeNumber</c> setting and
    /// read by <see cref="MessageCreateGatewayHandler"/> to drop messages from other shards.
    /// </summary>
    /// <param name="NodeNumber">
    /// The gateway shard id this node is responsible for. Horizontal scale-out assigns each worker a
    /// distinct value.
    /// </param>
    public record DiscordNodeConfig( int NodeNumber );

}
