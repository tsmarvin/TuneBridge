using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Implementations.DiscordGatewayHandlers;

namespace TuneBridge.Configuration {
    /// <summary>
    /// Represents the configuration for a Discord gateway node, containing the media link service and the node identifier.
    /// This is used by the <see cref="MessageCreateGatewayHandler"/> to process messages for a specific shard by node.
    /// </summary>
    /// <param name="LinkLookupService">The service used to resolve media links.</param>
    /// <param name="NodeNumber">The identifier for this node (used for sharding).</param>
    public record DiscordNodeConfig( IMediaLinkService LinkLookupService, int NodeNumber );
}
