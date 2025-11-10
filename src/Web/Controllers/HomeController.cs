using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;
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
        /// Performs music lookup and displays results in a server-rendered view.
        /// </summary>
        /// <param name="uri">Music URL(s) to look up.</param>
        /// <returns>Partial view with lookup results.</returns>
        [HttpPost]
        public async Task<IActionResult> LookupResults( string uri ) {
            if (_mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( uri )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "URI is required"
                } );
            }

            // Perform lookup server-side and collect all results
            MusicLookupViewModel viewModel = new( );
            await foreach (MediaLinkResult result in _mediaLinkService.GetInfoAsync( uri )) {
                if (result.Results.Count == 0) {
                    continue; // Skip empty results
                }

                // Find primary result
                MusicLookupResultDto? primaryResult = null;
                SupportedProviders primaryProvider = default;
                foreach ((SupportedProviders provider, MusicLookupResultDto dto) in result.Results) {
                    if (dto.IsPrimary) {
                        primaryResult = dto;
                        primaryProvider = provider;
                        break;
                    }
                    if (primaryResult == null) {
                        primaryResult = dto;
                        primaryProvider = provider;
                    }
                }

                if (primaryResult == null) {
                    continue;
                }

                // Store result and get card URL (server-side only)
                string? cardUrl = null;
                if (_cardService?.IsEnabled == true) {
                    cardUrl = _cardService.StoreResult( result );
                }

                viewModel.Items.Add( new MusicLookupViewModel.MusicLookupResultItem {
                    CardUrl = cardUrl,
                    Result = result,
                    PrimaryProvider = primaryProvider,
                    PrimaryResult = primaryResult
                } );
            }

            if (viewModel.Items.Count == 0) {
                viewModel.Message = "No results found";
            }

            return PartialView( "_LookupResults", viewModel );
        }

        /// <summary>
        /// Performs ISRC lookup and displays results in a server-rendered view.
        /// </summary>
        /// <param name="isrc">ISRC code to look up.</param>
        /// <returns>Partial view with lookup results.</returns>
        [HttpPost]
        public async Task<IActionResult> LookupResultsByIsrc( string isrc ) {
            if (_mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( isrc )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "ISRC is required"
                } );
            }

            MediaLinkResult? result = await _mediaLinkService.GetInfoByISRCAsync( isrc );
            return CreateViewModelFromResult( result, "No results found for ISRC" );
        }

        /// <summary>
        /// Performs UPC lookup and displays results in a server-rendered view.
        /// </summary>
        /// <param name="upc">UPC code to look up.</param>
        /// <returns>Partial view with lookup results.</returns>
        [HttpPost]
        public async Task<IActionResult> LookupResultsByUpc( string upc ) {
            if (_mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( upc )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "UPC is required"
                } );
            }

            MediaLinkResult? result = await _mediaLinkService.GetInfoByUPCAsync( upc );
            return CreateViewModelFromResult( result, "No results found for UPC" );
        }

        /// <summary>
        /// Performs title/artist lookup and displays results in a server-rendered view.
        /// </summary>
        /// <param name="title">Track or album title.</param>
        /// <param name="artist">Artist name.</param>
        /// <returns>Partial view with lookup results.</returns>
        [HttpPost]
        public async Task<IActionResult> LookupResultsByTitle( string title, string artist ) {
            if (_mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( title ) || string.IsNullOrWhiteSpace( artist )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Title and artist are required"
                } );
            }

            MediaLinkResult? result = await _mediaLinkService.GetInfoAsync( title, artist );
            return CreateViewModelFromResult( result, "No results found for title/artist" );
        }

        /// <summary>
        /// Helper method to create a view model from a MediaLinkResult.
        /// </summary>
        /// <param name="result">The lookup result.</param>
        /// <param name="noResultsMessage">Message to display when no results found.</param>
        /// <returns>Partial view with the created view model.</returns>
        private IActionResult CreateViewModelFromResult( MediaLinkResult? result, string noResultsMessage ) {
            MusicLookupViewModel viewModel = new( );

            if (result == null || result.Results.Count == 0) {
                viewModel.Message = noResultsMessage;
                return PartialView( "_LookupResults", viewModel );
            }

            // Find primary result
            MusicLookupResultDto? primaryResult = null;
            SupportedProviders primaryProvider = default;
            foreach ((SupportedProviders provider, MusicLookupResultDto dto) in result.Results) {
                if (dto.IsPrimary) {
                    primaryResult = dto;
                    primaryProvider = provider;
                    break;
                }
                if (primaryResult == null) {
                    primaryResult = dto;
                    primaryProvider = provider;
                }
            }

            if (primaryResult == null) {
                viewModel.Message = noResultsMessage;
                return PartialView( "_LookupResults", viewModel );
            }

            // Store result and get card URL
            string? cardUrl = null;
            if (_cardService?.IsEnabled == true) {
                cardUrl = _cardService.StoreResult( result );
            }

            viewModel.Items.Add( new MusicLookupViewModel.MusicLookupResultItem {
                CardUrl = cardUrl,
                Result = result,
                PrimaryProvider = primaryProvider,
                PrimaryResult = primaryResult
            } );

            return PartialView( "_LookupResults", viewModel );
        }

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
        /// This endpoint performs the lookup server-side, stores all results in the card service,
        /// and returns multiple card URLs for rendering.
        /// </summary>
        /// <param name="req">Request containing the music URL(s) to look up.</param>
        /// <returns>JSON response with array of card URLs and results.</returns>
        [HttpPost( "/lookup/web" )]
        public async Task<IActionResult> WebLookup( [FromBody] WebLookupRequest req ) {
            if (_mediaLinkService == null) {
                return BadRequest( new { error = "Music lookup service not available" } );
            }

            if (string.IsNullOrWhiteSpace( req.Uri )) {
                return BadRequest( new { error = "URI is required" } );
            }

            // Perform lookup server-side and collect all results
            List<WebLookupResultItem> items = [];
            await foreach (MediaLinkResult result in _mediaLinkService.GetInfoAsync( req.Uri )) {
                if (result.Results.Count == 0) {
                    continue; // Skip empty results
                }

                // Store result and get card URL (server-side only)
                string? cardUrl = null;
                if (_cardService?.IsEnabled == true) {
                    cardUrl = _cardService.StoreResult( result );
                }

                items.Add( new WebLookupResultItem( cardUrl, result ) );
            }

            if (items.Count == 0) {
                return Ok( new { hasResults = false, message = "No results found" } );
            }

            return Ok( new { hasResults = true, items } );
        }

        /// <summary>
        /// Request for web-specific lookup.
        /// </summary>
        /// <param name="Uri">Music URL(s) to look up (can contain multiple URLs).</param>
        public record WebLookupRequest( string Uri );

        /// <summary>
        /// Individual result item with card URL and fallback data.
        /// </summary>
        /// <param name="CardUrl">URL to the stored OpenGraph card, if available.</param>
        /// <param name="FallbackData">The raw result data for fallback display.</param>
        public record WebLookupResultItem( string? CardUrl, MediaLinkResult FallbackData );

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
