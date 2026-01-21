using BridgeBeats.Contracts.Enums;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services;
using BridgeBeats.Worker.Discord;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Add Redis client from Aspire (for queue processing)
builder.AddRedisClient( "redis" );

// Read Discord credentials from configuration
string? discordToken = builder.Configuration["BridgeBeats:DiscordToken"];
int nodeNumber = builder.Configuration.GetValue( "BridgeBeats:NodeNumber", 0 );

// Validate credentials at startup
if (string.IsNullOrWhiteSpace( discordToken )) {
    throw new InvalidOperationException(
        "Discord token is required. Set BridgeBeats:DiscordToken."
    );
}

// Read base URL for OpenGraph card generation
string baseUrl = builder.Configuration.GetValue( "BridgeBeats:BaseUrl", string.Empty ) ?? string.Empty;

// Read worker configuration
bool useWorkerServices = builder.Configuration.GetValue( "BridgeBeats:Workers:UseWorkerServices", false );
bool spotifyWorkerEnabled = builder.Configuration.GetValue( "BridgeBeats:Workers:SpotifyWorkerEnabled", false );
bool appleMusicWorkerEnabled = builder.Configuration.GetValue( "BridgeBeats:Workers:AppleMusicWorkerEnabled", false );
bool tidalWorkerEnabled = builder.Configuration.GetValue( "BridgeBeats:Workers:TidalWorkerEnabled", false );

// Determine which providers are enabled based on worker configuration
HashSet<SupportedProviders> enabledProviders = [ ];
if (useWorkerServices) {
    // When using worker services, determine enabled providers from worker availability
    if (spotifyWorkerEnabled) {
        enabledProviders.Add( SupportedProviders.Spotify );
    }
    if (appleMusicWorkerEnabled) {
        enabledProviders.Add( SupportedProviders.AppleMusic );
    }
    if (tidalWorkerEnabled) {
        enabledProviders.Add( SupportedProviders.Tidal );
    }
} else {
    // When not using worker services, we can't determine enabled providers
    // The Discord worker requires at least one provider to function
    throw new InvalidOperationException(
        "Discord worker requires UseWorkerServices to be true and at least one provider worker to be enabled."
    );
}

if (enabledProviders.Count == 0) {
    throw new InvalidOperationException(
        "At least one provider worker must be enabled for the Discord worker to function."
    );
}

// Register BridgeBeats services for media link lookup and card generation
int cardCacheExpirationHours = builder.Configuration.GetValue( "BridgeBeats:CardCacheExpirationHours", 1 );
int cardCacheCleanupInterval = builder.Configuration.GetValue( "BridgeBeats:CardCacheCleanupInterval", 500 );
_ = builder.Services.AddBridgeBeatsServices(
    enabledProviders,
    useCaching: false, // Discord worker doesn't need caching
    baseUrl,
    cardCacheExpirationHours,
    cardCacheCleanupInterval
);

// Register Discord services
_ = builder.Services.AddDiscordServices( discordToken, nodeNumber );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

app.Run( );
