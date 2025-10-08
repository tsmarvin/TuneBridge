using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// API controller for music lookup operations.
/// </summary>
/// <param name="svc">The media link service for performing music lookups.</param>
[ApiController]
[Route( "music/lookup" )]
public class MusicLookupController( IMediaLinkService svc ) : ControllerBase {

    private readonly IMediaLinkService _svc = svc;

    /// <summary>Request payload containing a URI to look up.</summary>
    public record UrlReq( string Uri );

    /// <summary>Request payload containing an ISRC to look up.</summary>
    public record IsrcReq( string Isrc );

    /// <summary>Request payload containing a UPC to look up.</summary>
    public record UpcReq( string Upc );

    /// <summary>Request payload containing a title and artist to look up.</summary>
    public record TitleReq( string Title, string Artist );

    /// <summary>
    /// Looks up music information from a list of URLs and returns all results.
    /// </summary>
    /// <param name="req">The request containing the URI(s) to parse.</param>
    /// <returns>A list of media link results.</returns>
    [HttpPost( "urlList" )]
    public async Task<IActionResult> ByUrlList( [FromBody] UrlReq req ) {
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            results.Add( result );
        }

        return Ok( results );
    }

    /// <summary>
    /// Looks up music information from URLs and streams results as they become available.
    /// </summary>
    /// <param name="req">The request containing the URI(s) to parse.</param>
    /// <returns>An async enumerable of media link results.</returns>
    [HttpPost( "url" )]
    public async IAsyncEnumerable<MediaLinkResult> ByUrl( [FromBody] UrlReq req ) {
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            yield return result;
        }
    }

    /// <summary>
    /// Looks up track information by ISRC.
    /// </summary>
    /// <param name="req">The request containing the ISRC.</param>
    /// <returns>The media link result if found.</returns>
    [HttpPost( "isrc" )]
    public async Task<IActionResult> ByIsrc( [FromBody] IsrcReq req )
        => Ok( await _svc.GetInfoByISRCAsync( req.Isrc ) );

    /// <summary>
    /// Looks up album information by UPC.
    /// </summary>
    /// <param name="req">The request containing the UPC.</param>
    /// <returns>The media link result if found.</returns>
    [HttpPost( "upc" )]
    public async Task<IActionResult> ByUpc( [FromBody] UpcReq req )
        => Ok( await _svc.GetInfoByUPCAsync( req.Upc ) );

    /// <summary>
    /// Looks up music information by title and artist.
    /// </summary>
    /// <param name="req">The request containing the title and artist.</param>
    /// <returns>The media link result if found.</returns>
    [HttpPost( "title" )]
    public async Task<IActionResult> ByTitle( [FromBody] TitleReq req )
        => Ok( await _svc.GetInfoAsync( req.Title, req.Artist ) );
}
