using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers {

    /// <summary>
    /// Controller for serving OpenGraph embeddable music cards.
    /// These cards display music metadata and provider links that can be embedded
    /// in any platform supporting OpenGraph (Discord, Slack, Twitter, etc.).
    /// </summary>
    /// <param name="cardService">Service for retrieving stored MediaLinkResult data.</param>
    [Route( "music/card" )]
    public class MusicCardController( IMediaCardService cardService ) : Controller {

        private readonly IMediaCardService _cardService = cardService;

        /// <summary>
        /// Displays an OpenGraph embeddable card for a specific music lookup result.
        /// The page includes OpenGraph meta tags that allow rich previews in platforms like Discord.
        /// </summary>
        /// <param name="id">The unique identifier for the MediaLinkResult to display.</param>
        /// <returns>
        /// HTTP 200 with the card view if the result is found.
        /// HTTP 404 if the ID is invalid or the result has expired.
        /// </returns>
        /// <response code="200">Card found and displayed successfully.</response>
        /// <response code="404">Card not found or expired.</response>
        [HttpGet( "{id}" )]
        public IActionResult Card( string id ) {
            MediaLinkResult? result = _cardService.GetResult( id );
            
            if (result == null) {
                return NotFound( );
            }

            return View( result );
        }
    }

}
