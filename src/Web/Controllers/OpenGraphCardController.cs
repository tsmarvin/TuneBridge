using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Implementations.Utilities;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for serving OpenGraph embeddable cards for music links.
/// </summary>
[Route( "card" )]
public class OpenGraphCardController( IOpenGraphCardService cardService, IMediaLinkCacheRepository? cacheRepository = null, ILogger<OpenGraphCardController>? logger = null ) : Controller {

    private readonly IOpenGraphCardService _cardService = cardService;
    private readonly IMediaLinkCacheRepository? _cacheRepository = cacheRepository;
    private readonly ILogger<OpenGraphCardController>? _logger = logger;

    /// <summary>
    /// Displays an OpenGraph embeddable card for a stored MediaLinkResult.
    /// </summary>
    /// <param name="id">The unique identifier of the stored result.</param>
    /// <returns>An HTML page with OpenGraph metadata and provider links.</returns>
    [HttpGet( "{id}" )]
    public async Task<IActionResult> Card( string id ) {
        MediaLinkResult? result = _cardService.GetResult( id );

        if (result == null) {
            return NotFound( "Card not found or expired" );
        }

        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );
        ViewBag.Metadata = metadata;
        ViewBag.BaseUrl = _cardService.BaseUrl;

        // Try to get ATProto URI from cache
        ViewBag.ATProtoUri = await GetATProtoUriFromCache( result );

        return View( result );
    }

    /// <summary>
    /// Attempts to retrieve the ATProto URI for a MediaLinkResult from the cache.
    /// Tries multiple lookup strategies: external ID (ISRC/UPC) and metadata.
    /// </summary>
    /// <param name="result">The MediaLinkResult to find in cache.</param>
    /// <returns>The ATProto URI if found in cache, otherwise null.</returns>
    private async Task<string?> GetATProtoUriFromCache( MediaLinkResult result ) {
        try {
            // Don't include input link strategy for card pages (no input links available)
            return await ATProtoUriHelper.GetATProtoUriFromCacheAsync( result, _cacheRepository, includeInputLinkStrategy: false );
        } catch (InvalidOperationException ex) {
            _logger?.LogWarning( ex, "Failed to retrieve ATProto URI from cache for card (InvalidOperationException)" );
        } catch (ArgumentException ex) {
            _logger?.LogWarning( ex, "Failed to retrieve ATProto URI from cache for card (ArgumentException)" );
        } catch (Exception ex) {
            _logger?.LogWarning( ex, "Failed to retrieve ATProto URI from cache for card" );
        }

        return null;
    }
}
