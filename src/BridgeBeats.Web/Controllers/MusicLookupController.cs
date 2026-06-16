using AspNetCore.Authentication.ApiKey;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Web API controller for programmatic music lookups, rooted at <c>music/lookup</c>. Resolves media links
/// across providers by URL, ISRC, UPC, or title/artist. Authenticated via API key or internal-service
/// scheme; the two URL endpoints additionally allow anonymous access.
/// </summary>
/// <param name="svc">The media-link service that performs the cross-provider lookups.</param>
[ApiController]
[Authorize( AuthenticationSchemes = ApiKeyDefaults.AuthenticationScheme + "," + InternalServiceDefaults.AuthenticationScheme )]
[IgnoreAntiforgeryToken]
[Route( "music/lookup" )]
public class MusicLookupController( IMediaLinkService svc ) : ControllerBase {

    /// <summary>The media-link service that performs the cross-provider lookups.</summary>
    private readonly IMediaLinkService _svc = svc;

    /// <summary>Request payload for a URL lookup.</summary>
    /// <param name="Uri">Free text that may contain one or more provider URLs (Spotify, Apple Music); every <c>https://</c> link found is extracted and resolved.</param>
    public record UrlReq( string Uri );

    /// <summary>Request payload for an ISRC lookup.</summary>
    /// <param name="Isrc">The 12-character International Standard Recording Code (e.g. "USRC17607839") to resolve.</param>
    public record IsrcReq( string Isrc );

    /// <summary>Request payload for a UPC lookup.</summary>
    /// <param name="Upc">The Universal Product Code barcode number (typically 12-13 digits, e.g. "00602537518357") to resolve.</param>
    public record UpcReq( string Upc );

    /// <summary>Request payload for a title/artist lookup.</summary>
    /// <param name="Title">The track or album title to resolve.</param>
    /// <param name="Artist">The artist name to resolve.</param>
    public record TitleReq( string Title, string Artist );

    /// <summary>
    /// Resolves every provider URL found in the request text and returns all results as a buffered list.
    /// Results are deduplicated on external IDs (ISRC for tracks, UPC for albums), so multiple URLs pointing
    /// to the same content are merged.
    /// </summary>
    /// <param name="req">The request containing the URL text, bound from the JSON request body.</param>
    /// <returns>HTTP POST <c>music/lookup/urlList</c>. <c>200 OK</c> with the list of results. Allows anonymous access.</returns>
    [AllowAnonymous]
    [HttpPost( "urlList" )]
    public async Task<IActionResult> ByUrlList( [FromBody] UrlReq req ) {
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            results.Add( result );
        }

        return Ok( results );
    }

    /// <summary>
    /// Resolves every provider URL found in the request text and streams results as they are produced.
    /// </summary>
    /// <param name="req">The request containing the URL text, bound from the JSON request body.</param>
    /// <returns>HTTP POST <c>music/lookup/url</c>. An asynchronous stream of media-link results. Allows anonymous access.</returns>
    [AllowAnonymous]
    [HttpPost( "url" )]
    public async IAsyncEnumerable<MediaLinkResult> ByUrl( [FromBody] UrlReq req ) {
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            yield return result;
        }
    }

    /// <summary>
    /// Resolves a recording by ISRC, the most reliable cross-platform identifier when known.
    /// </summary>
    /// <param name="req">The request containing the ISRC, bound from the JSON request body.</param>
    /// <returns>
    /// HTTP POST <c>music/lookup/isrc</c>. <c>200 OK</c> with the result, or a result carrying a
    /// "No results found for ISRC." message when nothing matches. Requires API-key or internal-service auth.
    /// </returns>
    [HttpPost( "isrc" )]
    public async Task<IActionResult> ByIsrc( [FromBody] IsrcReq req ) {
        MediaLinkResult? result = await _svc.GetInfoByISRCAsync( req.Isrc );
        return Ok( result ?? new MediaLinkResult { Messages = ["No results found for ISRC."] } );
    }

    /// <summary>
    /// Resolves a release by UPC, useful for distinguishing editions of the same album.
    /// </summary>
    /// <param name="req">The request containing the UPC, bound from the JSON request body.</param>
    /// <returns>
    /// HTTP POST <c>music/lookup/upc</c>. <c>200 OK</c> with the result, or a result carrying a
    /// "No results found for UPC." message when nothing matches. Requires API-key or internal-service auth.
    /// </returns>
    [HttpPost( "upc" )]
    public async Task<IActionResult> ByUpc( [FromBody] UpcReq req ) {
        MediaLinkResult? result = await _svc.GetInfoByUPCAsync( req.Upc );
        return Ok( result ?? new MediaLinkResult { Messages = ["No results found for UPC."] } );
    }

    /// <summary>
    /// Resolves a track or album by title and artist across all configured providers.
    /// </summary>
    /// <param name="req">The request containing the title and artist, bound from the JSON request body.</param>
    /// <returns>
    /// HTTP POST <c>music/lookup/title</c>. <c>200 OK</c> with the result, or a result carrying a
    /// "No results found for title/artist." message when nothing matches. Requires API-key or internal-service auth.
    /// </returns>
    [HttpPost( "title" )]
    public async Task<IActionResult> ByTitle( [FromBody] TitleReq req ) {
        MediaLinkResult? result = await _svc.GetInfoAsync( req.Title, req.Artist );
        return Ok( result ?? new MediaLinkResult { Messages = ["No results found for title/artist."] } );
    }
}
