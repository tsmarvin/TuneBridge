using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers;

[ApiController]
[Route( "music/lookup" )]
public class MusicLookupController( IMediaLinkService svc ) : ControllerBase {

    private readonly IMediaLinkService _svc = svc;

    public record UrlReq( string Uri );
    public record IsrcReq( string Isrc );
    public record UpcReq( string Upc );
    public record TitleReq( string Title, string Artist );

    [HttpPost( "urlList" )]
    public async Task<IActionResult> ByUrlList( [FromBody] UrlReq req ) {
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            results.Add( result );
        }

        return Ok( results );
    }

    [HttpPost( "url" )]
    public async IAsyncEnumerable<MediaLinkResult> ByUrl( [FromBody] UrlReq req ) {
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            yield return result;
        }
    }

    [HttpPost( "isrc" )]
    public async Task<IActionResult> ByIsrc( [FromBody] IsrcReq req )
        => Ok( await _svc.GetInfoByISRCAsync( req.Isrc ) );

    [HttpPost( "upc" )]
    public async Task<IActionResult> ByUpc( [FromBody] UpcReq req )
        => Ok( await _svc.GetInfoByUPCAsync( req.Upc ) );

    [HttpPost( "title" )]
    public async Task<IActionResult> ByTitle( [FromBody] TitleReq req )
        => Ok( await _svc.GetInfoAsync( req.Title, req.Artist ) );
}
