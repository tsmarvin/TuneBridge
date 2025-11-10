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
    /// Request containing a MediaLinkResult to store for card generation.
    /// </summary>
    /// <param name="Result">The media link result to store.</param>
    public record StoreResultRequest( MediaLinkResult Result );

    /// <summary>
    /// Stores a MediaLinkResult and returns the card URL for web interface use.
    /// </summary>
    /// <param name="req">Request containing the result to store.</param>
    /// <returns>JSON response with the card URL.</returns>
    [HttpPost( "store" )]
    public IActionResult StoreResult( [FromBody] StoreResultRequest req ) {
        if (req?.Result == null) {
            return BadRequest( new { error = "Result is required" } );
        }

        if (!_cardService.IsEnabled) {
            return BadRequest( new { error = "OpenGraph card service is not enabled" } );
        }

        string cardUrl = _cardService.StoreResult( req.Result );
        return Ok( new { cardUrl } );
    }

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

        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );
        ViewBag.Metadata = metadata;

        return View( result );
    }
}
