using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Web API controller for storing Open Graph card results. Rooted at <c>api/card</c> and used by the Discord
/// worker to persist a media-link result and obtain a shareable card URL.
/// </summary>
/// <param name="cardService">The Open Graph card service used to check availability and store results.</param>
[ApiController]
[Route( "api/card" )]
public class CardApiController( IOpenGraphCardService cardService ) : ControllerBase {

    /// <summary>The Open Graph card service used to check availability and store results.</summary>
    private readonly IOpenGraphCardService _cardService = cardService;

    /// <summary>
    /// Request payload for storing a card.
    /// </summary>
    /// <param name="Result">The media-link result to store as a card.</param>
    public record StoreCardRequest( MediaLinkResult Result );

    /// <summary>
    /// Response payload returned after attempting to store a card.
    /// </summary>
    /// <param name="CardUrl">The URL of the stored card, or <see langword="null"/> when storage was unavailable or the request was invalid.</param>
    public record StoreCardResponse( string? CardUrl );

    /// <summary>
    /// Stores a media-link result as an Open Graph card and returns its URL.
    /// </summary>
    /// <param name="result">The media-link result to store, bound from the JSON request body.</param>
    /// <returns>
    /// HTTP POST <c>api/card/store</c>. <c>200 OK</c> with the card URL on success; <c>503 Service Unavailable</c>
    /// when the card service is disabled; <c>400 Bad Request</c> when the result is null or empty. Anti-forgery
    /// validation is ignored for this endpoint.
    /// </returns>
    [HttpPost( "store" )]
    [IgnoreAntiforgeryToken]
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
