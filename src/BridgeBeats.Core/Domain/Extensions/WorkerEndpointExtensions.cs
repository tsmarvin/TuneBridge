using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records.WorkerApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BridgeBeats.Core.Domain.Extensions;

/// <summary>
/// Maps the worker-side HTTP server for the per-provider lookup contract. These endpoints are the
/// server counterpart to the <c>HttpMusicLookupService</c> proxy used by the web app: the proxy
/// posts to <c>/lookup/*</c>, a worker handles the request with a direct
/// <see cref="BridgeBeats.Contracts.Interfaces.IMusicLookupService"/>, and the result is returned in
/// a <see cref="BridgeBeats.Contracts.Records.WorkerApi.ProviderLookupResponse"/> envelope.
/// </summary>
public static class WorkerEndpointExtensions {

    /// <summary>
    /// Maps the six <c>/lookup/*</c> minimal-API routes (<c>url</c>, <c>isrc</c>, <c>upc</c>,
    /// <c>id</c>, <c>metadata</c>, <c>from-result</c>) onto a direct
    /// <typeparamref name="TService"/> resolved per request from the request services. Each route
    /// binds its request record, calls the matching lookup method, and wraps the outcome in a
    /// <see cref="BridgeBeats.Contracts.Records.WorkerApi.ProviderLookupResponse"/>.
    /// </summary>
    /// <typeparam name="TService">
    /// The concrete direct lookup service that handles the requests; resolved from request services
    /// on each call.
    /// </typeparam>
    /// <param name="app">The endpoint route builder to map the routes onto.</param>
    /// <returns>The same <paramref name="app"/>, to allow call chaining.</returns>
    /// <remarks>
    /// Every route returns HTTP 200 regardless of outcome — successes carry the result and failures
    /// carry an error envelope (see <see cref="ExecuteLookupAsync"/>). Callers must read the
    /// envelope's success flag, not the HTTP status code, to tell success from failure.
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
    /// Runs a single lookup and wraps its outcome in a
    /// <see cref="BridgeBeats.Contracts.Records.WorkerApi.ProviderLookupResponse"/> returned as HTTP
    /// 200. A successful call yields an <c>Ok</c> envelope; any thrown exception is caught and
    /// returned as an <c>Error</c> envelope (still HTTP 200), so the success flag in the envelope is
    /// the authoritative outcome signal.
    /// </summary>
    /// <param name="lookupFunc">The lookup to invoke.</param>
    /// <returns>An HTTP 200 result carrying the success or error envelope.</returns>
    private static async Task<IResult> ExecuteLookupAsync( Func<Task<MusicLookupResult?>> lookupFunc ) {
        try {
            MusicLookupResult? result = await lookupFunc( );
            return Results.Ok( ProviderLookupResponse.Ok( result ) );
        } catch (Exception ex) {
            return Results.Ok( ProviderLookupResponse.Error( ex.Message ) );
        }
    }
}
