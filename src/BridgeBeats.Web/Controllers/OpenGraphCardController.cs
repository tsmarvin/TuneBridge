using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for serving OpenGraph embeddable cards for music links.
/// </summary>
[Route( "card" )]
public partial class OpenGraphCardController(
    IOpenGraphCardService cardService,
    IQrCodeService qrCodeService,
    IMediaLinkCacheRepository? cacheRepository = null,
    ILogger<OpenGraphCardController>? logger = null
) : Controller {

    private readonly IOpenGraphCardService _cardService = cardService;
    private readonly IQrCodeService _qrCodeService = qrCodeService;
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

        // Fallback to persistent cache if not in memory
        if (result == null && _cacheRepository != null) {
            (MediaLinkResult result, string recordUri, bool isStale)? cached = await _cacheRepository.TryGetCachedResultByCardIdAsync( id );
            if (cached.HasValue) {
                result = cached.Value.result;
                // Repopulate in-memory store for subsequent requests
                _ = _cardService.StoreResult( result );
            }
        }

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
    /// Displays an embeddable compact card view for a stored MediaLinkResult.
    /// This endpoint is designed for iframe embedding and shows the music lookup card style.
    /// </summary>
    /// <param name="id">The unique identifier of the stored result.</param>
    /// <returns>A minimal HTML page with just the card suitable for iframe embedding.</returns>
    [HttpGet( "{id}/embed" )]
    public async Task<IActionResult> Embed( string id ) {
        return await RenderEmbed( id, useQrCode: false );
    }

    /// <summary>
    /// Displays an embeddable compact card view with a QR code replacing the artwork image.
    /// The QR code links to the embed URL for easy mobile scanning.
    /// </summary>
    /// <param name="id">The unique identifier of the stored result.</param>
    /// <returns>A minimal HTML page with the card showing a QR code instead of artwork.</returns>
    [HttpGet( "{id}/embed/qr" )]
    public async Task<IActionResult> EmbedQr( string id ) {
        return await RenderEmbed( id, useQrCode: true );
    }

    /// <summary>
    /// Shared implementation for rendering embed views with or without QR code.
    /// </summary>
    private async Task<IActionResult> RenderEmbed( string id, bool useQrCode ) {
        MediaLinkResult? result = _cardService.GetResult( id );

        // Fallback to persistent cache if not in memory
        if (result == null && _cacheRepository != null) {
            (MediaLinkResult result, string recordUri, bool isStale)? cached = await _cacheRepository.TryGetCachedResultByCardIdAsync( id );
            if (cached.HasValue) {
                result = cached.Value.result;
                // Repopulate in-memory store for subsequent requests
                _ = _cardService.StoreResult( result );
            }
        }

        if (result == null) {
            return NotFound( "Card not found or expired" );
        }

        // Get the primary provider and result for display
        SupportedProviders primaryProvider = result.Results.Keys.FirstOrDefault( );
        MusicLookupResult? primaryResult = result.Results.Values.FirstOrDefault( );

        if (primaryResult == null) {
            return NotFound( "No results found" );
        }

        // Try to get ATProto URI from cache
        string? atProtoUri = await GetATProtoUriFromCache( result );

        string cardUrl = $"https://{_cardService.BaseUrl}/card/{id}";

        // Generate QR code data URI if requested
        string? qrCodeDataUri = null;
        if (useQrCode) {
            qrCodeDataUri = _qrCodeService.GenerateQrCodeDataUri( cardUrl );
        }

        // Create a view model for the embed view
        var viewModel = new {
            Result = result,
            PrimaryProvider = primaryProvider,
            PrimaryResult = primaryResult,
            CardUrl = cardUrl,
            ATProtoUri = atProtoUri,
            QrCodeDataUri = qrCodeDataUri
        };

        return View( "Embed", viewModel );
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
            LogCacheInvalidOp( ex );
        } catch (ArgumentException ex) {
            LogCacheArgError( ex );
        } catch (Exception ex) {
            LogCacheError( ex );
        }

        return null;
    }
}
