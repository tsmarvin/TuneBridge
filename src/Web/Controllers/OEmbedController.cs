using System.Text.Encodings.Web;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Domain.Types.Enums;
using Microsoft.AspNetCore.Mvc;

namespace BridgeBeats.Web.Controllers;

/// <summary>
/// Controller for serving oEmbed responses for music lookup result cards.
/// Implements the oEmbed specification: https://oembed.com/
/// </summary>
[Route( "oembed" )]
public class OEmbedController( IOpenGraphCardService cardService, ILogger<OEmbedController>? logger = null ) : Controller {

    private readonly IOpenGraphCardService _cardService = cardService;
    private readonly ILogger<OEmbedController>? _logger = logger;

    private const int DefaultEmbedWidth = 550;
    private const int DefaultEmbedHeight = 250;
    private const int CardExpirationDays = 6;
    private const int CardCacheAgeSeconds = CardExpirationDays * 24 * 60 * 60; // 6 days in seconds

    /// <summary>
    /// Returns oEmbed metadata for a given card URL.
    /// </summary>
    /// <param name="url">The URL of the card to embed (e.g., https://bridgebeats.link/card/{id}).</param>
    /// <param name="maxwidth">Optional maximum width in pixels for the embed.</param>
    /// <param name="maxheight">Optional maximum height in pixels for the embed.</param>
    /// <param name="format">Response format (json or xml). Only json is supported.</param>
    /// <returns>oEmbed response with rich HTML for embedding.</returns>
    /// <response code="200">Successfully generated oEmbed response.</response>
    /// <response code="400">Invalid URL parameter or unsupported format.</response>
    /// <response code="404">Card not found or expired.</response>
    [HttpGet]
    [Produces( "application/json" )]
    public IActionResult GetOEmbed(
        [FromQuery] string url,
        [FromQuery] int? maxwidth,
        [FromQuery] int? maxheight,
        [FromQuery] string? format
    ) {
        // Validate format parameter (only JSON is supported)
        if (!string.IsNullOrWhiteSpace( format ) && !format.Equals( "json", StringComparison.OrdinalIgnoreCase )) {
            _logger?.LogWarning( "Unsupported oEmbed format requested: {Format}", format );
            return BadRequest( new { error = "Only JSON format is supported" } );
        }

        // Validate URL parameter
        if (string.IsNullOrWhiteSpace( url )) {
            _logger?.LogWarning( "oEmbed request missing URL parameter" );
            return BadRequest( new { error = "URL parameter is required" } );
        }

        // Parse the card ID from the URL
        // Expected format: https://bridgebeats.link/card/{id} or /card/{id}
        string? cardId = ExtractCardIdFromUrl( url );
        if (string.IsNullOrWhiteSpace( cardId )) {
            _logger?.LogWarning( "Could not extract card ID from URL: {Url}", url );
            return BadRequest( new { error = "Invalid card URL format" } );
        }

        // Retrieve the card data
        MediaLinkResult? result = _cardService.GetResult( cardId );
        if (result == null) {
            _logger?.LogInformation( "Card not found for ID: {CardId}", cardId );
            return NotFound( new { error = "Card not found or expired" } );
        }

        // Get primary result for metadata
        KeyValuePair<SupportedProviders, MusicLookupResultDto> primaryEntry = result.Results.First( );
        MusicLookupResultDto primaryResult = primaryEntry.Value;

        if (primaryResult == null) {
            _logger?.LogWarning( "No results found in card data for ID: {CardId}", cardId );
            return NotFound( new { error = "No results found in card" } );
        }

        // Calculate embed dimensions
        int embedWidth = maxwidth ?? DefaultEmbedWidth;
        int embedHeight = maxheight ?? DefaultEmbedHeight;

        // Ensure dimensions are within reasonable bounds
        embedWidth = Math.Max( 200, Math.Min( embedWidth, 1000 ) );
        embedHeight = Math.Max( 150, Math.Min( embedHeight, 600 ) );

        // Generate the embed HTML
        string embedUrl = $"https://{_cardService.BaseUrl}/card/{cardId}/embed";
        string embedHtml = GenerateEmbedHtml( embedUrl, embedWidth, embedHeight, primaryResult.Title );

        // Build the oEmbed response
        OEmbedResponse response = new( ) {
            Version = "1.0",
            Type = "rich",
            Title = primaryResult.Title,
            AuthorName = primaryResult.Artist,
            ProviderName = "BridgeBeats",
            ProviderUrl = $"https://{_cardService.BaseUrl}",
            Width = embedWidth,
            Height = embedHeight,
            Html = embedHtml,
            ThumbnailUrl = primaryResult.ArtUrl,
            ThumbnailWidth = 300,
            ThumbnailHeight = 300,
            CacheAge = CardCacheAgeSeconds
        };

        return Ok( response );
    }

    /// <summary>
    /// Extracts the card ID from a card URL.
    /// Supports both full URLs (https://bridgebeats.link/card/{id}) and relative paths (/card/{id}).
    /// </summary>
    /// <param name="url">The card URL to parse.</param>
    /// <returns>The card ID, or null if the URL format is invalid.</returns>
    private static string? ExtractCardIdFromUrl( string url ) {
        try {
            // Try to parse as URI to handle both absolute and relative URLs
            if (Uri.TryCreate( url, UriKind.Absolute, out Uri? absoluteUri )) {
                // Absolute URL: extract path
                string path = absoluteUri.AbsolutePath;
                return ExtractCardIdFromPath( path );
            } else if (Uri.TryCreate( url, UriKind.Relative, out Uri? relativeUri )) {
                // Relative URL
                return ExtractCardIdFromPath( url );
            }

            // Fallback: try to extract directly if it looks like a path
            return ExtractCardIdFromPath( url );
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Extracts the card ID from a URL path.
    /// </summary>
    /// <param name="path">The URL path (e.g., /card/{id}).</param>
    /// <returns>The card ID, or null if the path format is invalid.</returns>
    private static string? ExtractCardIdFromPath( string path ) {
        // Expected format: /card/{id} or /card/{id}/embed
        const string cardPrefix = "/card/";
        int startIndex = path.IndexOf( cardPrefix, StringComparison.OrdinalIgnoreCase );

        if (startIndex == -1) {
            return null;
        }

        int idStart = startIndex + cardPrefix.Length;
        int slashIndex = path.IndexOf( '/', idStart );

        // Extract ID (either until next slash or end of string)
        string id = slashIndex > idStart
            ? path[idStart..slashIndex]
            : path[idStart..];

        return string.IsNullOrWhiteSpace( id ) ? null : id;
    }

    /// <summary>
    /// Generates the HTML for embedding the card in an iframe.
    /// </summary>
    /// <param name="embedUrl">The URL to the embed endpoint.</param>
    /// <param name="width">The width of the iframe in pixels.</param>
    /// <param name="height">The height of the iframe in pixels.</param>
    /// <param name="title">The title for the iframe (for accessibility).</param>
    /// <returns>HTML string for the iframe embed.</returns>
    private static string GenerateEmbedHtml( string embedUrl, int width, int height, string title ) {
        // Use HtmlEncoder to ensure proper escaping
        string encodedUrl = HtmlEncoder.Default.Encode( embedUrl );
        string encodedTitle = HtmlEncoder.Default.Encode( title );

        return $"<iframe src=\"{encodedUrl}\" width=\"{width}\" height=\"{height}\" frameborder=\"0\" allowtransparency=\"true\" title=\"{encodedTitle}\"></iframe>";
    }
}
