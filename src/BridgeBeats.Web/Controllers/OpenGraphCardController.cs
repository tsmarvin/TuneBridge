using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Serves shareable Open Graph cards for media-link results, rooted at <c>card</c>. Renders the full card
/// page and embeddable variants (with or without a QR code), falling back to the cache and re-storing
/// results when a card is not in memory.
/// </summary>
/// <param name="cardService">The Open Graph card service used to retrieve and store card results and to supply the public domain.</param>
/// <param name="qrCodeService">Service that generates QR-code data URIs for embeddable cards.</param>
/// <param name="cacheRepository">Optional cache repository used to recover a result by card id and to resolve its ATProto URI.</param>
/// <param name="logger">Optional logger for cache-resolution warnings.</param>
[Route( "card" )]
public partial class OpenGraphCardController(
    IOpenGraphCardService cardService,
    IQrCodeService qrCodeService,
    IMediaLinkCacheRepository? cacheRepository = null,
    ILogger<OpenGraphCardController>? logger = null
) : Controller {

    /// <summary>The Open Graph card service used to retrieve and store card results and to supply the public domain.</summary>
    private readonly IOpenGraphCardService _cardService = cardService;
    /// <summary>Service that generates QR-code data URIs for embeddable cards.</summary>
    private readonly IQrCodeService _qrCodeService = qrCodeService;
    /// <summary>Optional cache repository used to recover a result by card id and to resolve its ATProto URI.</summary>
    private readonly IMediaLinkCacheRepository? _cacheRepository = cacheRepository;
    /// <summary>Optional logger for cache-resolution warnings.</summary>
    private readonly ILogger<OpenGraphCardController>? _logger = logger;

    /// <summary>
    /// Renders the full Open Graph card page for the given card id, populating Open Graph metadata and the
    /// ATProto URI. When the card is not held in memory it is recovered from the cache and re-stored.
    /// </summary>
    /// <param name="id">The card id from the route.</param>
    /// <returns>
    /// HTTP GET <c>card/{id}</c>. The card view on success; <c>404 Not Found</c> when the card is unknown or expired.
    /// </returns>
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
        ViewBag.Domain = _cardService.Domain;

        // Try to get ATProto URI from cache
        ViewBag.ATProtoUri = await GetATProtoUriFromCache( result );

        return View( result );
    }

    /// <summary>
    /// Renders the embeddable card view for the given card id, without a QR code.
    /// </summary>
    /// <param name="id">The card id from the route.</param>
    /// <returns>
    /// HTTP GET <c>card/{id}/embed</c>. The embed view on success; <c>404 Not Found</c> when the card or its
    /// results are unavailable.
    /// </returns>
    [HttpGet( "{id}/embed" )]
    public async Task<IActionResult> Embed( string id ) {
        return await RenderEmbed( id, useQrCode: false );
    }

    /// <summary>
    /// Renders the embeddable card view for the given card id, including a QR code that links to the card.
    /// </summary>
    /// <param name="id">The card id from the route.</param>
    /// <returns>
    /// HTTP GET <c>card/{id}/embed/qr</c>. The embed view with a QR code on success; <c>404 Not Found</c>
    /// when the card or its results are unavailable.
    /// </returns>
    [HttpGet( "{id}/embed/qr" )]
    public async Task<IActionResult> EmbedQr( string id ) {
        return await RenderEmbed( id, useQrCode: true );
    }

    /// <summary>
    /// Builds the embed view model for a card, recovering the result from the cache when needed, selecting the
    /// primary provider, resolving the ATProto URI, and optionally generating a QR code.
    /// </summary>
    /// <param name="id">The card id to render.</param>
    /// <param name="useQrCode">Whether to generate and include a QR-code data URI in the view model.</param>
    /// <returns>The embed view on success; <c>404 Not Found</c> when the card or its results are unavailable.</returns>
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

        string cardUrl = $"https://{_cardService.Domain}/card/{id}";

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
    /// Attempts to resolve the ATProto record URI for a result from the cache, swallowing cache errors so a
    /// missing URI does not break the response. The input-link strategy is disabled for card rendering.
    /// </summary>
    /// <param name="result">The media-link result whose ATProto URI is being resolved.</param>
    /// <returns>The ATProto URI when available; otherwise <see langword="null"/>.</returns>
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
