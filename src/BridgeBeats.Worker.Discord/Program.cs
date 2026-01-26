using BridgeBeats.ServiceDefaults;
using BridgeBeats.Worker.Discord;
using BridgeBeats.Worker.Discord.Services;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Configure file logging
_ = builder.ConfigureFileLogging( "Discord" );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Read Discord configuration
string? discordToken = builder.Configuration["BridgeBeats:DiscordToken"];
int nodeNumber = builder.Configuration.GetValue( "BridgeBeats:NodeNumber", 0 );
string baseUrl = builder.Configuration["BridgeBeats:BaseUrl"] ?? string.Empty;

// Validate Discord token at startup
if (string.IsNullOrWhiteSpace( discordToken )) {
    throw new InvalidOperationException(
        "Discord token is required. Set BridgeBeats:DiscordToken."
    );
}

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
    IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>( );
    HttpClient httpClient = factory.CreateClient( nameof( BridgeBeatsApiClient ) );
    ILogger<BridgeBeatsApiClient> logger = sp.GetRequiredService<ILogger<BridgeBeatsApiClient>>( );
    return new BridgeBeatsApiClient( httpClient, logger, baseUrl );
} );

// Configure Discord gateway
_ = builder.Services.AddDiscordShardedGateway( options => {
    options.Token = discordToken;
    options.Intents = GatewayIntents.GuildMessages | GatewayIntents.MessageContent;
} );

// Register gateway handlers from this assembly
_ = builder.Services.AddShardedGatewayHandlers( typeof( MessageCreateGatewayHandler ).Assembly );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

try {
    app.Run( );
} finally {
    Log.CloseAndFlush( );
}
