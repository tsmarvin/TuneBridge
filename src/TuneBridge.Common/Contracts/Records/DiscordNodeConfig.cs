using TuneBridge.Common.Contracts.Interfaces;

namespace TuneBridge.Common.Contracts.Records {
    /// <summary>
    /// Represents the configuration for a Discord gateway node, containing the media link service and the node identifier.
    /// </summary>
    /// <param name="LinkLookupService">The service used to resolve media links.</param>
    /// <param name="NodeNumber">The identifier for this node (used for sharding).</param>
    public record DiscordNodeConfig( IMediaLinkService LinkLookupService, int NodeNumber );
}
