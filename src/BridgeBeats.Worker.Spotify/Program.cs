using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.DTOs.WorkerApi;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Infrastructure.Cache;
using BridgeBeats.Providers.Spotify;
using BridgeBeats.ServiceDefaults;
using BridgeBeats.Services.Queue;
using BridgeBeats.Worker.Spotify;
using Serilog;

WebApplicationBuilder builder = WebApplication.CreateBuilder( args );

// Configure file logging
_ = builder.ConfigureFileLogging( "Spotify" );

// Add Aspire service defaults (health checks, telemetry, resilience)
_ = builder.AddServiceDefaults( );

// Add Redis client from Aspire (for queue processing)
builder.AddRedisClient( "redis" );

// Read Spotify credentials from configuration
string? clientId = builder.Configuration["BridgeBeats:SpotifyClientId"];
string? clientSecret = builder.Configuration["BridgeBeats:SpotifyClientSecret"];
int maxRetryAfterSeconds = builder.Configuration.GetValue( "BridgeBeats:Resilience:MaxRetryAfterSeconds", 120 );

// Validate credentials at startup
if (string.IsNullOrWhiteSpace( clientId ) || string.IsNullOrWhiteSpace( clientSecret )) {
    throw new InvalidOperationException(
        "Spotify credentials are required. Set BridgeBeats:SpotifyClientId and BridgeBeats:SpotifyClientSecret."
    );
}

// Register Spotify services
HashSet<SupportedProviders> enabledProviders = [ ];
_ = builder.Services.AddSpotifyServices( clientId, clientSecret, enabledProviders, maxRetryAfterSeconds );

// Register JSON serializer options (required by SpotifyLookupService)
_ = builder.Services.AddSingleton( new JsonSerializerOptions { WriteIndented = true } );

// Register genre cache for artist genre processing
_ = builder.Services.AddGenreCache( );

// Register queue processor background service for consuming from Redis streams
_ = builder.Services.AddQueueProcessor<SpotifyLookupService>( SupportedProviders.Spotify );

// Register Spotify-specific batch queue helper and bulk processor service
_ = builder.Services.AddSingleton<SpotifyBatchQueueHelper>( );
_ = builder.Services.AddHostedService<SpotifyBulkProcessorService>( );

// Register Spotify artist genre service for scheduled genre fetching
_ = builder.Services.AddHostedService<SpotifyArtistGenreService>( );

WebApplication app = builder.Build( );

// Map Aspire health check endpoints
_ = app.MapDefaultEndpoints( );

// Resolve the Spotify lookup service
SpotifyLookupService spotifyService = app.Services.GetRequiredService<SpotifyLookupService>( );

// POST /lookup/url - Lookup by provider URL
app.MapPost( "/lookup/url", async ( LookupByUrlRequest request ) => {
    try {
        MusicLookupResult? result = await spotifyService.GetInfoAsync( request.Url );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/isrc - Lookup by ISRC
app.MapPost( "/lookup/isrc", async ( LookupByIsrcRequest request ) => {
    try {
        MusicLookupResult? result = await spotifyService.GetInfoByISRCAsync( request.Isrc );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/upc - Lookup by UPC
app.MapPost( "/lookup/upc", async ( LookupByUpcRequest request ) => {
    try {
        MusicLookupResult? result = await spotifyService.GetInfoByUPCAsync( request.Upc );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/id - Lookup by provider-specific ID
app.MapPost( "/lookup/id", async ( LookupByIdRequest request ) => {
    try {
        MusicLookupResult? result = await spotifyService.GetInfoByIDAsync( request.ProviderId, request.IsAlbum );
        return Results.Ok( ProviderLookupResponse.Ok( result ) );
    } catch (Exception ex) {
        return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
    }
} );

// POST /lookup/metadata - Lookup by title and artist
app.MapPost( "/lookup/metadata", async ( LookupByMetadataRequest request ) => {
    try {
        MusicLookupResult? result = await spotifyService.GetInfoAsync( request.Title, request.Artist );
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
        MusicLookupResult? result = await spotifyService.GetInfoAsync( lookup );
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
