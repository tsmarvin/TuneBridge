using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Worker.Discord.Services;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Serilog;

namespace BridgeBeats.Worker.Discord;

/// <summary>
/// Entry point for the Discord worker service.
/// </summary>
public static class Program {

    /// <summary>
    /// The main entry point for the Discord worker application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static void Main( string[] args ) {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder( args );

        ConfigureServices( builder );

        IHost app = builder.Build();

        try {
            app.Run( );
        } finally {
            Log.CloseAndFlush( );
        }
    }

    /// <summary>
    /// Configures the services for the Discord worker application.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    private static void ConfigureServices( HostApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "Discord" );

        // Add Aspire service defaults (telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Read and validate configuration
        (string discordToken, int nodeNumber, string baseUrl) = ValidateConfiguration( builder );

        // Register Discord node configuration
        _ = builder.Services.AddSingleton( new DiscordNodeConfig( nodeNumber ) );

        // Configure HTTP client for BridgeBeats Web API
        // Uses Aspire service discovery to resolve "bridgebeats" service
        _ = builder.Services.AddHttpClient<BridgeBeatsApiClient>( client => {
            // The base address will be set via Aspire service discovery
            client.BaseAddress = new Uri( "http://bridgebeats" );
            client.DefaultRequestHeaders.Add( "User-Agent", "BridgeBeats-Discord-Worker/1.0" );
        } ).AddStandardResilienceHandler( );

        // Register BridgeBeatsApiClient with base URL for card generation
        _ = builder.Services.AddSingleton( sp => {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient(nameof(BridgeBeatsApiClient));
            ILogger<BridgeBeatsApiClient> logger = sp.GetRequiredService<ILogger<BridgeBeatsApiClient>>();
            return new BridgeBeatsApiClient( httpClient, logger, baseUrl );
        } );

        // Configure Discord gateway
        _ = builder.Services.AddDiscordShardedGateway( options => {
            options.Token = discordToken;
            options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
        } );

        // Register gateway handlers from this assembly
        _ = builder.Services.AddShardedGatewayHandlers( typeof( MessageCreateGatewayHandler ).Assembly );
    }

    /// <summary>
    /// Validates the required configuration for the Discord worker.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>A tuple containing the validated Discord configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
    private static (string DiscordToken, int NodeNumber, string BaseUrl) ValidateConfiguration(
        HostApplicationBuilder builder
    ) {
        string? discordToken = builder.Configuration["BridgeBeats:DiscordToken"];
        int nodeNumber = builder.Configuration.GetValue("BridgeBeats:NodeNumber", 0);
        string baseUrl = builder.Configuration["BridgeBeats:BaseUrl"] ?? string.Empty;

        return string.IsNullOrWhiteSpace( discordToken )
            ? throw new InvalidOperationException(
                "Discord token is required. Set BridgeBeats:DiscordToken."
            )
            : ((string DiscordToken, int NodeNumber, string BaseUrl))(discordToken, nodeNumber, baseUrl);
    }
}
