using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Configuration;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace BridgeBeats.Web.Discord {
    /// <summary>
    /// Extension methods for configuring Discord services.
    /// </summary>
    public static class DiscordServiceExtensions {
        /// <summary>
        /// Adds Discord services to the service collection if Discord token is configured.
        /// </summary>
        /// <param name="services">The service collection to add services to.</param>
        /// <param name="config">The configuration containing Discord settings.</param>
        /// <param name="environmentName">The current environment name.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDiscordServices(
            this IServiceCollection services,
            IConfiguration config,
            string environmentName
        ) {
            // Skip Discord registration in Testing environment
            if (environmentName == "Testing") {
                return services;
            }

            AppSettings settings = new( );
            config.GetRequiredSection( "BridgeBeats" ).Bind( settings );

            // Only register Discord services if token is provided
            if (string.IsNullOrWhiteSpace( settings.DiscordToken )) {
                return services;
            }

            _ = services.AddTransient( s => new DiscordNodeConfig(
                s.GetRequiredService<IMediaLinkService>( ),
                settings.NodeNumber
            ) );

            _ = services.AddDiscordShardedGateway( options => {
                options.Token = settings.DiscordToken;
                options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
            } );

            _ = services.AddShardedGatewayHandlers( typeof( DiscordServiceExtensions ).Assembly );

            return services;
        }
    }
}
