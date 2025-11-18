using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Common.Contracts.Constants;
using TuneBridge.Common.Contracts.DTOs;
using TuneBridge.Common.Contracts.Enums;
using TuneBridge.Common.Contracts.Interfaces;
using TuneBridge.Common.Utilities;
using TuneBridge.Web.Models;

namespace TuneBridge.Web.Controllers {
    /// <summary>
    /// Controller for the main web application pages.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="HomeController"/> class.
    /// </remarks>
    /// <param name="logger">The logger for recording diagnostic information.</param>
    /// <param name="mediaLinkService">Optional music lookup service.</param>
    /// <param name="cardService">Optional OpenGraph card service.</param>
    /// <param name="cacheRepository">Optional cache repository for ATProto URIs.</param>
    public class HomeController(
        ILogger<HomeController> logger,
        IMediaLinkService? mediaLinkService = null,
        IOpenGraphCardService? cardService = null,
        IMediaLinkCacheRepository? cacheRepository = null
    ) : Controller {

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
            if (mediaLinkService == null) {
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
            await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( uri )) {
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
                if (cardService?.IsEnabled == true) {
                    cardUrl = cardService.StoreResult( result );
                }

                // Get ATProto URI from cache if available
                string? atProtoUri = await GetATProtoUriFromCache( result );

                viewModel.Items.Add( new MusicLookupViewModel.MusicLookupResultItem {
                    CardUrl = cardUrl,
                    ATProtoUri = atProtoUri,
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
            if (mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( isrc )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "ISRC is required"
                } );
            }

            MediaLinkResult? result = await mediaLinkService.GetInfoByISRCAsync( isrc );
            return await CreateViewModelFromResult( result, "No results found for ISRC" );
        }

        /// <summary>
        /// Performs UPC lookup and displays results in a server-rendered view.
        /// </summary>
        /// <param name="upc">UPC code to look up.</param>
        /// <returns>Partial view with lookup results.</returns>
        [HttpPost]
        public async Task<IActionResult> LookupResultsByUpc( string upc ) {
            if (mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( upc )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "UPC is required"
                } );
            }

            MediaLinkResult? result = await mediaLinkService.GetInfoByUPCAsync( upc );
            return await CreateViewModelFromResult( result, "No results found for UPC" );
        }

        /// <summary>
        /// Performs title/artist lookup and displays results in a server-rendered view.
        /// </summary>
        /// <param name="title">Track or album title.</param>
        /// <param name="artist">Artist name.</param>
        /// <returns>Partial view with lookup results.</returns>
        [HttpPost]
        public async Task<IActionResult> LookupResultsByTitle( string title, string artist ) {
            if (mediaLinkService == null) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Music lookup service not available"
                } );
            }

            if (string.IsNullOrWhiteSpace( title ) || string.IsNullOrWhiteSpace( artist )) {
                return PartialView( "_LookupResults", new MusicLookupViewModel {
                    Message = "Title and artist are required"
                } );
            }

            MediaLinkResult? result = await mediaLinkService.GetInfoAsync( title, artist );
            return await CreateViewModelFromResult( result, "No results found for title/artist" );
        }

        /// <summary>
        /// Helper method to create a view model from a MediaLinkResult.
        /// </summary>
        /// <param name="result">The lookup result.</param>
        /// <param name="noResultsMessage">Message to display when no results found.</param>
        /// <returns>Partial view with the created view model.</returns>
        private async Task<IActionResult> CreateViewModelFromResult( MediaLinkResult? result, string noResultsMessage ) {
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
            if (cardService?.IsEnabled == true) {
                cardUrl = cardService.StoreResult( result );
            }

            // Get ATProto URI from cache if available
            string? atProtoUri = await GetATProtoUriFromCache( result );

            viewModel.Items.Add( new MusicLookupViewModel.MusicLookupResultItem {
                CardUrl = cardUrl,
                ATProtoUri = atProtoUri,
                Result = result,
                PrimaryProvider = primaryProvider,
                PrimaryResult = primaryResult
            } );

            return PartialView( "_LookupResults", viewModel );
        }

        /// <summary>
        /// Attempts to retrieve the ATProto URI for a MediaLinkResult from the cache.
        /// Tries multiple lookup strategies: input link, external ID (ISRC/UPC), and metadata.
        /// </summary>
        /// <param name="result">The MediaLinkResult to find in cache.</param>
        /// <returns>The ATProto URI if found in cache, otherwise null.</returns>
        private async Task<string?> GetATProtoUriFromCache( MediaLinkResult result ) {
            try {
                return await ATProtoUriHelper.GetATProtoUriFromCacheAsync( result, cacheRepository );
            } catch (InvalidOperationException ex) {
                logger.LogWarning( ex, "Failed to retrieve ATProto URI from cache due to invalid operation, continuing without it" );
            } catch (ArgumentException ex) {
                logger.LogWarning( ex, "Failed to retrieve ATProto URI from cache due to argument error, continuing without it" );
            } catch (Exception ex) {
                logger.LogWarning( ex, "Failed to retrieve ATProto URI from cache, continuing without it" );
            }

            return null;
        }

        /// <summary>
        /// Displays the privacy policy page.
        /// </summary>
        /// <returns>The privacy view.</returns>
        public IActionResult Privacy( ) => View( );

        /// <summary>
        /// Displays the terms of service page.
        /// </summary>
        /// <returns>The TOS view.</returns>
        public IActionResult Tos( ) => View( );

        /// <summary>
        /// Health check endpoint for monitoring and load balancers.
        /// </summary>
        /// <returns>HTTP 200 OK with a simple status message.</returns>
        [HttpGet( EndpointPaths.Health )]
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
            if (mediaLinkService == null) {
                return BadRequest( new { error = "Music lookup service not available" } );
            }

            if (string.IsNullOrWhiteSpace( req.Uri )) {
                return BadRequest( new { error = "URI is required" } );
            }

            // Perform lookup server-side and collect all results
            List<WebLookupResultItem> items = [];
            await foreach (MediaLinkResult result in mediaLinkService.GetInfoAsync( req.Uri )) {
                if (result.Results.Count == 0) {
                    continue; // Skip empty results
                }

                // Store result and get card URL (server-side only)
                string? cardUrl = null;
                if (cardService?.IsEnabled == true) {
                    cardUrl = cardService.StoreResult( result );
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
