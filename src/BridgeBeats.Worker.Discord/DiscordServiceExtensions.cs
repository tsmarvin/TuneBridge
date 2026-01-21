using BridgeBeats.Contracts.Interfaces;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace BridgeBeats.Worker.Discord {
    /// <summary>
    /// Extension methods for configuring Discord services.
    /// </summary>
    public static class DiscordServiceExtensions {
        /// <summary>
        /// Adds Discord services to the service collection.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="discordToken">The Discord bot token.</param>
        /// <param name="nodeNumber">The node number for sharding.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDiscordServices(
            this IServiceCollection services,
            string discordToken,
            int nodeNumber
        ) {
            // Register Discord node configuration
            _ = services.AddTransient( s => new DiscordNodeConfig(
                s.GetRequiredService<IMediaLinkService>( ),
                nodeNumber
            ) );

            // Register Discord gateway with sharding support
            _ = services.AddDiscordShardedGateway( options => {
                options.Token = discordToken;
                options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
            } );

            // Register gateway event handlers
            _ = services.AddShardedGatewayHandlers( typeof( DiscordServiceExtensions ).Assembly );

            return services;
        }
    }
}
