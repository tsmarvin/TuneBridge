using System.Diagnostics;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Worker.Discord.Services;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Serilog;

namespace BridgeBeats.Worker.Discord;

/// <summary>
/// Entry point and composition root for the Discord worker. Builds a background host (the worker
/// exposes no HTTP endpoint), wires the sharded Discord gateway and the BridgeBeats Web API client,
/// and runs until shutdown.
/// </summary>
public static class Program {

    /// <summary>
    /// Builds and runs the Discord worker host, flushing Serilog on exit.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the host builder.</param>
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
    /// Registers the worker's services: file logging, Aspire service defaults, the
    /// <see cref="DiscordNodeConfig"/> singleton, the <see cref="BridgeBeatsApiClient"/> typed HTTP
    /// client (base address <c>http://bridgebeats</c>, 130-second timeout, internal service-key
    /// header, trace propagation disabled), and the sharded Discord gateway plus its message
    /// handlers.
    /// </summary>
    /// <param name="builder">The host application builder being configured.</param>
    private static void ConfigureServices( HostApplicationBuilder builder ) {
        // Configure file logging
        _ = builder.ConfigureFileLogging( "Discord" );

        // Add Aspire service defaults (telemetry, resilience)
        _ = builder.AddServiceDefaults( );

        // Read and validate configuration
        (string discordToken, int nodeNumber, string domain) = ValidateConfiguration( builder );

        // Register Discord node configuration
        _ = builder.Services.AddSingleton( new DiscordNodeConfig( nodeNumber ) );

        // Configure HTTP client for BridgeBeats Web API
        // Uses Aspire service discovery to resolve "bridgebeats" service
        string? internalServiceKey = builder.Configuration["BridgeBeats:InternalServiceKey"];
        _ = builder.Services.AddHttpClient<BridgeBeatsApiClient>( client => {
            // Base address resolved via Aspire service discovery.
            client.BaseAddress = new Uri( "http://bridgebeats" );
            // Transport backstop above the global 120s AttemptTimeout so the resilience pipeline is the binding constraint.
            client.Timeout = TimeSpan.FromSeconds( 130 );
            client.DefaultRequestHeaders.Add( "User-Agent", "BridgeBeats-Discord-Worker/1.0" );
            if (!string.IsNullOrWhiteSpace( internalServiceKey )) {
                client.DefaultRequestHeaders.Add( "X-Service-Key", internalServiceKey );
            }
        } )
        .ConfigurePrimaryHttpMessageHandler( ( ) => new SocketsHttpHandler {
            // Prevent DiagnosticsHandler from injecting non-ASCII trace context headers
            // (e.g. tracestate, baggage) that cause HttpRequestException at the socket level.
            ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator( )
        } );

        // Register BridgeBeatsApiClient with base URL for card generation
        _ = builder.Services.AddSingleton( sp => {
            IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
            HttpClient httpClient = factory.CreateClient(nameof(BridgeBeatsApiClient));
            ILogger<BridgeBeatsApiClient> logger = sp.GetRequiredService<ILogger<BridgeBeatsApiClient>>();
            return new BridgeBeatsApiClient( httpClient, logger, domain );
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
    /// Reads and validates the worker's required configuration: the Discord token
    /// (<c>BridgeBeats:DiscordToken</c>, mandatory), the gateway node number
    /// (<c>BridgeBeats:NodeNumber</c>, defaulting to 0), and the card-link domain
    /// (<c>BridgeBeats:Domain</c>, optional).
    /// </summary>
    /// <param name="builder">The host application builder whose configuration is read.</param>
    /// <returns>The validated Discord token, node number, and domain.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the Discord token is missing or blank.</exception>
    private static (string DiscordToken, int NodeNumber, string Domain) ValidateConfiguration(
        HostApplicationBuilder builder
    ) {
        string? discordToken = builder.Configuration["BridgeBeats:DiscordToken"];
        int nodeNumber = builder.Configuration.GetValue("BridgeBeats:NodeNumber", 0);
        string domain = builder.Configuration["BridgeBeats:Domain"] ?? string.Empty;

        return string.IsNullOrWhiteSpace( discordToken )
            ? throw new InvalidOperationException(
                "Discord token is required. Set BridgeBeats:DiscordToken."
            )
            : ((string DiscordToken, int NodeNumber, string Domain))(discordToken, nodeNumber, domain);
    }
}
