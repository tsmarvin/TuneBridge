using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// API controller for OpenGraph card storage operations.
/// Used by the Discord worker to store cards and retrieve card URLs.
/// </summary>
[ApiController]
[Route( "api/card" )]
public class CardApiController( IOpenGraphCardService cardService ) : ControllerBase {

    private readonly IOpenGraphCardService _cardService = cardService;

    /// <summary>
    /// Request to store a MediaLinkResult and generate a card URL.
    /// </summary>
    public record StoreCardRequest( MediaLinkResult Result );

    /// <summary>
    /// Response containing the generated card URL.
    /// </summary>
    /// <param name="CardUrl">The full URL to the OpenGraph card.</param>
    public record StoreCardResponse( string? CardUrl );

    /// <summary>
    /// Stores a MediaLinkResult and returns the generated OpenGraph card URL.
    /// </summary>
    /// <param name="result">The media link result to store.</param>
    /// <returns>The card URL for the stored result.</returns>
    /// <response code="200">Card stored successfully.</response>
    /// <response code="400">Invalid request body.</response>
    /// <response code="503">Card service is not enabled.</response>
    [HttpPost( "store" )]
    public IActionResult Store( [FromBody] MediaLinkResult result ) {
        if (!_cardService.IsEnabled) {
            return StatusCode( 503, new StoreCardResponse( null ) );
        }

        if (result == null || result.Results.Count == 0) {
            return BadRequest( new StoreCardResponse( null ) );
        }

        string cardUrl = _cardService.StoreResult( result );
        return Ok( new StoreCardResponse( cardUrl ) );
    }
}
