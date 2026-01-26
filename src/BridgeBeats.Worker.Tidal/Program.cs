using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.DTOs.WorkerApi;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.Tidal;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Configure file logging
_ = builder.ConfigureFileLogging( "Tidal" );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Add Redis client from Aspire (for queue processing)
builder.AddRedisClient( "redis" );

// Read Tidal credentials from configuration
string? clientId = builder.Configuration["BridgeBeats:TidalClientId"];
string? clientSecret = builder.Configuration["BridgeBeats:TidalClientSecret"];
int maxRetryAfterSeconds = builder.Configuration.GetValue( "BridgeBeats:Resilience:MaxRetryAfterSeconds", 120 );

// Validate credentials at startup
if (string.IsNullOrWhiteSpace( clientId ) || string.IsNullOrWhiteSpace( clientSecret )) {
    throw new InvalidOperationException(
        "Tidal credentials are required. Set BridgeBeats:TidalClientId and BridgeBeats:TidalClientSecret."
    );
}

// Register Tidal services
HashSet<SupportedProviders> enabledProviders = [ ];
_ = builder.Services.AddTidalServices( clientId, clientSecret, enabledProviders, maxRetryAfterSeconds );

// Register JSON serializer options (required by TidalLookupService)
_ = builder.Services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

// Register queue processor background service for consuming from Redis streams
_ = builder.Services.AddQueueProcessor<TidalLookupService>( SupportedProviders.Tidal );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

// Resolve the Tidal lookup service
TidalLookupService tidalService = app.Services.GetRequiredService<TidalLookupService>( );

// POST /lookup/url - Lookup by provider URL
app.MapPost( "/lookup/url", async ( LookupByUrlRequest request ) => {
    try {
        MusicLookupResult? result = await tidalService.GetInfoAsync( request.Url );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/isrc - Lookup by ISRC
app.MapPost( "/lookup/isrc", async ( LookupByIsrcRequest request ) => {
    try {
        MusicLookupResult? result = await tidalService.GetInfoByISRCAsync( request.Isrc );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/upc - Lookup by UPC
app.MapPost( "/lookup/upc", async ( LookupByUpcRequest request ) => {
    try {
        MusicLookupResult? result = await tidalService.GetInfoByUPCAsync( request.Upc );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/id - Lookup by provider-specific ID
app.MapPost( "/lookup/id", async ( LookupByIdRequest request ) => {
    try {
        MusicLookupResult? result = await tidalService.GetInfoByIDAsync( request.ProviderId, request.IsAlbum );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/metadata - Lookup by title and artist
app.MapPost( "/lookup/metadata", async ( LookupByMetadataRequest request ) => {
    try {
        MusicLookupResult? result = await tidalService.GetInfoAsync( request.Title, request.Artist );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/from-result - Lookup from existing result (cross-platform matching)
app.MapPost( "/lookup/from-result", async ( LookupFromResultRequest request ) => {
    try {
        // Create a MusicLookupResult from the request to pass to the service
        MusicLookupResult lookup = new( ) {
            Artist = request.Artist,
            Title = request.Title,
            ExternalId = request.ExternalId ?? string.Empty,
            IsAlbum = request.IsAlbum
        };
        MusicLookupResult? result = await tidalService.GetInfoAsync( lookup );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

try {
    app.Run( );
} finally {
    Log.CloseAndFlush( );
}
