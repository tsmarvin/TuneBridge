using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records.WorkerApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BridgeBeats.Core.Domain.Extensions;

/// <summary>
/// Extension methods for mapping provider lookup endpoints in worker applications.
/// </summary>
public static class WorkerEndpointExtensions {

    /// <summary>
    /// Maps the standard set of provider lookup endpoints for a given lookup service type.
    /// </summary>
    /// <typeparam name="TService">
    /// The type of the lookup service that implements <see cref="IMusicLookupService"/>.
    /// </typeparam>
    /// <param name="app">The <see cref="IEndpointRouteBuilder"/> to add routes to.</param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> for chaining.</returns>
    /// <remarks>
    /// Maps the following endpoints:
    /// <list type="bullet">
    /// <item><description>POST /lookup/url - Lookup by provider URL</description></item>
    /// <item><description>POST /lookup/isrc - Lookup by ISRC</description></item>
    /// <item><description>POST /lookup/upc - Lookup by UPC</description></item>
    /// <item><description>POST /lookup/id - Lookup by provider-specific ID</description></item>
    /// <item><description>POST /lookup/metadata - Lookup by title and artist</description></item>
    /// <item><description>POST /lookup/from-result - Cross-platform matching from existing result</description></item>
    /// </list>
    /// </remarks>
    public static IEndpointRouteBuilder MapProviderLookupEndpoints<TService>(
        this IEndpointRouteBuilder app
    ) where TService : class, IMusicLookupService {
        // Resolve service per-request instead of at startup to avoid lifecycle issues
        _ = app.MapPost( "/lookup/url", async ( LookupByUrlRequest request, HttpContext context ) => {
            TService service = context.RequestServices.GetRequiredService<TService>( );
            return await ExecuteLookupAsync( ( ) => service.GetInfoAsync( request.Url ) );
        } );

        _ = app.MapPost( "/lookup/isrc", async ( LookupByIsrcRequest request, HttpContext context ) => {
            TService service = context.RequestServices.GetRequiredService<TService>( );
            return await ExecuteLookupAsync( ( ) => service.GetInfoByISRCAsync( request.Isrc ) );
        } );

        _ = app.MapPost( "/lookup/upc", async ( LookupByUpcRequest request, HttpContext context ) => {
            TService service = context.RequestServices.GetRequiredService<TService>( );
            return await ExecuteLookupAsync( ( ) => service.GetInfoByUPCAsync( request.Upc ) );
        } );

        _ = app.MapPost( "/lookup/id", async ( LookupByIdRequest request, HttpContext context ) => {
            TService service = context.RequestServices.GetRequiredService<TService>( );
            return await ExecuteLookupAsync( ( ) => service.GetInfoByIDAsync( request.ProviderId, request.IsAlbum ) );
        } );

        _ = app.MapPost( "/lookup/metadata", async ( LookupByMetadataRequest request, HttpContext context ) => {
            TService service = context.RequestServices.GetRequiredService<TService>( );
            return await ExecuteLookupAsync( ( ) => service.GetInfoAsync( request.Title, request.Artist ) );
        } );

        _ = app.MapPost( "/lookup/from-result", async ( LookupFromResultRequest request, HttpContext context ) => {
            TService service = context.RequestServices.GetRequiredService<TService>( );
            MusicLookupResult lookup = new( ) {
                Artist = request.Artist,
                Title = request.Title,
                ExternalId = request.ExternalId ?? string.Empty,
                IsAlbum = request.IsAlbum
            };
            return await ExecuteLookupAsync( ( ) => service.GetInfoAsync( lookup ) );
        } );

        return app;
    }

    /// <summary>
    /// Executes a lookup operation with standardized error handling.
    /// </summary>
    /// <param name="lookupFunc">The async function that performs the lookup.</param>
    /// <returns>An <see cref="IResult"/> containing the lookup response.</returns>
    private static async Task<IResult> ExecuteLookupAsync( Func<Task<MusicLookupResult?>> lookupFunc ) {
        try {
            MusicLookupResult? result = await lookupFunc( );
            return Results.Ok( ProviderLookupResponse.Ok( result ) );
        } catch (Exception ex) {
            return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
        }
    }
}
