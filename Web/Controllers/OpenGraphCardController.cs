using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for serving OpenGraph embeddable cards for music links.
/// </summary>
[Route( "card" )]
public class OpenGraphCardController( IOpenGraphCardService cardService ) : Controller {

    private readonly IOpenGraphCardService _cardService = cardService;

    /// <summary>
    /// Displays an OpenGraph embeddable card for a stored MediaLinkResult.
    /// </summary>
    /// <param name="id">The unique identifier of the stored result.</param>
    /// <returns>An HTML page with OpenGraph metadata and provider links.</returns>
    [HttpGet( "{id}" )]
    public IActionResult Card( string id ) {
        MediaLinkResult? result = _cardService.GetResult( id );
        
        if (result == null) {
            return NotFound( "Card not found or expired" );
        }

        var metadata = result.ToOpenGraphMetadata();
        ViewBag.Metadata = metadata;
        ViewBag.Result = result;

        return View( result );
    }
}
