using System.Diagnostics;
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
        (string discordToken, int nodeNumber, string domain) = ValidateConfiguration( builder );

        // Register Discord node configuration
        _ = builder.Services.AddSingleton( new DiscordNodeConfig( nodeNumber ) );

        // Configure HTTP client for BridgeBeats Web API
        // Uses Aspire service discovery to resolve "bridgebeats" service
        string? internalServiceKey = builder.Configuration["BridgeBeats:InternalServiceKey"];
        _ = builder.Services.AddHttpClient<BridgeBeatsApiClient>( client => {
            // The base address will be set via Aspire service discovery
            client.BaseAddress = new Uri( "http://bridgebeats" );
            // Multi-link lookups wait server-side for up to 90s in total (see
            // LookupOrchestrator.LookupByContentAsync) - keep the transport timeout above
            // that so the server-side budget, not HttpClient's default 100s, is the
            // binding constraint
            client.Timeout = TimeSpan.FromSeconds( 120 );
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
    /// Validates the required configuration for the Discord worker.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>A tuple containing the validated Discord configuration.</returns>
    /// <exception cref="InvalidOperationException">Thrown when required credentials are missing.</exception>
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
