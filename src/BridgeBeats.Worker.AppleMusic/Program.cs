using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.DTOs.WorkerApi;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Providers.AppleMusic;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Configure file logging
_ = builder.ConfigureFileLogging( "AppleMusic" );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Add Redis client from Aspire (for queue processing)
builder.AddRedisClient( "redis" );

// Read Apple Music credentials from configuration
string? teamId = builder.Configuration["BridgeBeats:AppleTeamId"];
string? keyId = builder.Configuration["BridgeBeats:AppleKeyId"];
string? keyPath = builder.Configuration["BridgeBeats:AppleKeyPath"];
int maxRetryAfterSeconds = builder.Configuration.GetValue( "BridgeBeats:Resilience:MaxRetryAfterSeconds", 120 );

// Validate credentials at startup
if (string.IsNullOrWhiteSpace( teamId ) ||
    string.IsNullOrWhiteSpace( keyId ) ||
    string.IsNullOrWhiteSpace( keyPath )) {
    throw new InvalidOperationException(
        "Apple Music credentials are required. Set BridgeBeats:AppleTeamId, BridgeBeats:AppleKeyId, and BridgeBeats:AppleKeyPath."
    );
}

// Register Apple Music services
HashSet<SupportedProviders> enabledProviders = [ ];
_ = builder.Services.AddAppleMusicServices( teamId, keyId, keyPath, enabledProviders, maxRetryAfterSeconds );

// Register JSON serializer options (required by AppleMusicLookupService)
_ = builder.Services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

// Register queue processor background service for consuming from Redis streams
_ = builder.Services.AddQueueProcessor<AppleMusicLookupService>( SupportedProviders.AppleMusic );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

// Resolve the Apple Music lookup service
AppleMusicLookupService appleMusicService = app.Services.GetRequiredService<AppleMusicLookupService>( );

// POST /lookup/url - Lookup by provider URL
app.MapPost( "/lookup/url", async ( LookupByUrlRequest request ) => {
    try {
        MusicLookupResult? result = await appleMusicService.GetInfoAsync( request.Url );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/isrc - Lookup by ISRC
app.MapPost( "/lookup/isrc", async ( LookupByIsrcRequest request ) => {
    try {
        MusicLookupResult? result = await appleMusicService.GetInfoByISRCAsync( request.Isrc );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/upc - Lookup by UPC
app.MapPost( "/lookup/upc", async ( LookupByUpcRequest request ) => {
    try {
        MusicLookupResult? result = await appleMusicService.GetInfoByUPCAsync( request.Upc );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/id - Lookup by provider-specific ID
app.MapPost( "/lookup/id", async ( LookupByIdRequest request ) => {
    try {
        MusicLookupResult? result = await appleMusicService.GetInfoByIDAsync( request.ProviderId, request.IsAlbum );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/metadata - Lookup by title and artist
app.MapPost( "/lookup/metadata", async ( LookupByMetadataRequest request ) => {
    try {
        MusicLookupResult? result = await appleMusicService.GetInfoAsync( request.Title, request.Artist );
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
        MusicLookupResult? result = await appleMusicService.GetInfoAsync( lookup );
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
