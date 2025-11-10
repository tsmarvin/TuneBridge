using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Web.Models;

namespace TuneBridge.Web.Controllers {
    /// <summary>
    /// Controller for the main web application pages.
    /// </summary>
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;
        private readonly IMediaLinkService? _mediaLinkService;
        private readonly IOpenGraphCardService? _cardService;

        /// <summary>
        /// Initializes a new instance of the <see cref="HomeController"/> class.
        /// </summary>
        /// <param name="logger">The logger for recording diagnostic information.</param>
        /// <param name="mediaLinkService">Optional music lookup service.</param>
        /// <param name="cardService">Optional OpenGraph card service.</param>
        public HomeController( ILogger<HomeController> logger, IMediaLinkService? mediaLinkService = null, IOpenGraphCardService? cardService = null ) {
            _logger = logger;
            _mediaLinkService = mediaLinkService;
            _cardService = cardService;
        }

        /// <summary>
        /// Displays the home/index page.
        /// </summary>
        /// <returns>The index view.</returns>
        public IActionResult Index( ) => View( );

        /// <summary>
        /// Displays the privacy policy page.
        /// </summary>
        /// <returns>The privacy view.</returns>
        public IActionResult Privacy( ) => View( );

        /// <summary>
        /// Health check endpoint for monitoring and load balancers.
        /// </summary>
        /// <returns>HTTP 200 OK with a simple status message.</returns>
        [HttpGet( "/health" )]
        [ResponseCache( Duration = 0, Location = ResponseCacheLocation.None, NoStore = true )]
        public IActionResult Health( ) {
            return Ok( new { status = "healthy", timestamp = DateTime.UtcNow } );
        }

        /// <summary>
        /// Web-specific lookup endpoint that returns results with card URLs for display.
        /// This endpoint performs the lookup server-side and stores results in the card service.
        /// </summary>
        /// <param name="req">Request containing the music URL to look up.</param>
        /// <returns>JSON response with card URL if available, otherwise error message.</returns>
        [HttpPost( "/lookup/web" )]
        public async Task<IActionResult> WebLookup( [FromBody] WebLookupRequest req ) {
            if (_mediaLinkService == null) {
                return BadRequest( new { error = "Music lookup service not available" } );
            }

            if (string.IsNullOrWhiteSpace( req.Uri )) {
                return BadRequest( new { error = "URI is required" } );
            }

            // Perform lookup server-side
            MediaLinkResult? result = null;
            await foreach (MediaLinkResult r in _mediaLinkService.GetInfoAsync( req.Uri )) {
                result = r;
                break; // Take first result for web interface
            }

            if (result == null || result.Results.Count == 0) {
                return Ok( new { hasResults = false, message = "No results found" } );
            }

            // Store result and get card URL (server-side only)
            string? cardUrl = null;
            if (_cardService?.IsEnabled == true) {
                cardUrl = _cardService.StoreResult( result );
            }

            return Ok( new { hasResults = true, cardUrl, fallbackData = result } );
        }

        /// <summary>
        /// Request for web-specific lookup.
        /// </summary>
        /// <param name="Uri">Music URL to look up.</param>
        public record WebLookupRequest( string Uri );

        /// <summary>
        /// Displays the error page.
        /// </summary>
        /// <returns>The error view with diagnostic information.</returns>
        [ResponseCache( Duration = 0, Location = ResponseCacheLocation.None, NoStore = true )]
        public IActionResult Error( ) {
            return View( new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier } );
        }
    }
}
