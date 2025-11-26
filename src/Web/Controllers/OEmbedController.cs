using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for oEmbed discovery and response endpoints.
/// Provides oEmbed responses for TuneBridge card URLs according to the oEmbed specification.
/// See: https://oembed.com/
/// </summary>
[Route( "oembed" )]
[ApiController]
public partial class OEmbedController( IOpenGraphCardService cardService ) : ControllerBase {

    private readonly IOpenGraphCardService _cardService = cardService;

    /// <summary>
    /// Default width for the embedded card.
    /// </summary>
    private const int DefaultWidth = 400;

    /// <summary>
    /// Default height for the embedded card.
    /// </summary>
    private const int DefaultHeight = 300;

    /// <summary>
    /// Maximum width for the embedded card.
    /// </summary>
    private const int MaxWidth = 800;

    /// <summary>
    /// Maximum height for the embedded card.
    /// </summary>
    private const int MaxHeight = 600;

    /// <summary>
    /// Cache age in seconds (6 days, matching the card storage expiry).
    /// </summary>
    private const int CacheAgeSeconds = 518400; // 6 days * 24 hours * 60 minutes * 60 seconds

    /// <summary>
    /// Default thumbnail size in pixels (album artwork is typically square).
    /// </summary>
    private const int DefaultThumbnailSize = 640;

    [GeneratedRegex( @"/card/([a-zA-Z0-9]+)(?:/embed)?$" )]
    private static partial Regex CardIdPattern( );

    /// <summary>
    /// Returns oEmbed data for a given TuneBridge card URL.
    /// </summary>
    /// <param name="url">The URL of the TuneBridge card to embed.</param>
    /// <param name="format">The response format (json or xml). Defaults to json.</param>
    /// <param name="maxwidth">The maximum width of the embedded resource.</param>
    /// <param name="maxheight">The maximum height of the embedded resource.</param>
    /// <returns>An oEmbed response with embed information.</returns>
    /// <response code="200">Successfully retrieved oEmbed data.</response>
    /// <response code="400">Invalid URL parameter.</response>
    /// <response code="404">Card not found or expired.</response>
    /// <response code="501">XML format not implemented.</response>
    [HttpGet]
    [ProducesResponseType( typeof( OEmbedResponse ), StatusCodes.Status200OK )]
    [ProducesResponseType( StatusCodes.Status400BadRequest )]
    [ProducesResponseType( StatusCodes.Status404NotFound )]
    [ProducesResponseType( StatusCodes.Status501NotImplemented )]
    public IActionResult GetOEmbed(
        [FromQuery] string url,
        [FromQuery] string format = "json",
        [FromQuery] int? maxwidth = null,
        [FromQuery] int? maxheight = null
    ) {
        // Validate format
        if (!string.Equals( format, "json", StringComparison.OrdinalIgnoreCase )) {
            // XML format is optional per oEmbed spec
            return StatusCode(
                StatusCodes.Status501NotImplemented,
                new { error = "Only JSON format is supported" }
            );
        }

        // Validate URL parameter
        if (string.IsNullOrWhiteSpace( url )) {
            return BadRequest( new { error = "URL parameter is required" } );
        }

        // Extract card ID from URL
        string? cardId = ExtractCardId( url );
        if (string.IsNullOrEmpty( cardId )) {
            return BadRequest( new { error = "Invalid card URL format" } );
        }

        // Get the stored result
        MediaLinkResult? result = _cardService.GetResult( cardId );
        if (result == null) {
            return NotFound( new { error = "Card not found or expired" } );
        }

        // Calculate embed dimensions
        int width = Math.Min( maxwidth ?? DefaultWidth, MaxWidth );
        int height = Math.Min( maxheight ?? DefaultHeight, MaxHeight );

        // Build oEmbed response
        OEmbedResponse response = BuildOEmbedResponse( result, cardId, width, height );

        return Ok( response );
    }

    /// <summary>
    /// Extracts the card ID from a TuneBridge card URL.
    /// Supports both /card/{id} and /card/{id}/embed URL formats.
    /// </summary>
    /// <param name="url">The card URL to parse.</param>
    /// <returns>The card ID if found, otherwise null.</returns>
    private static string? ExtractCardId( string url ) {
        try {
            // First decode the URL in case it's URL-encoded
            url = WebUtility.UrlDecode( url );

            // Use regex to extract card ID from the URL path
            Match match = CardIdPattern( ).Match( url );
            if (match.Success) {
                return match.Groups[1].Value;
            }

            return null;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Builds an oEmbed response from the given MediaLinkResult.
    /// </summary>
    /// <param name="result">The media link result containing track/album information.</param>
    /// <param name="cardId">The card ID for building URLs.</param>
    /// <param name="width">The embed width.</param>
    /// <param name="height">The embed height.</param>
    /// <returns>A complete oEmbed response object.</returns>
    private OEmbedResponse BuildOEmbedResponse(
        MediaLinkResult result,
        string cardId,
        int width,
        int height
    ) {
        // Get metadata from the result
        Dictionary<string, string> metadata = result.ToOpenGraphMetadata( );

        // Extract key fields
        string? title = metadata.GetValueOrDefault( "og:title" );
        string? artist = metadata.GetValueOrDefault( "music:musician" );
        string? image = metadata.GetValueOrDefault( "og:image" );

        // Build the embed URL
        string baseUrl = _cardService.BaseUrl.TrimEnd( '/' );
        string embedUrl = $"https://{baseUrl}/card/{cardId}/embed";
        string cardUrl = $"https://{baseUrl}/card/{cardId}";
        string providerUrl = $"https://{baseUrl}";

        // Build the iframe HTML for rich embed
        string html = BuildIframeHtml( embedUrl, cardUrl, title ?? "Music Link", width, height );

        return new OEmbedResponse {
            Type = "rich",
            Version = "1.0",
            Title = title,
            AuthorName = artist,
            ProviderName = "TuneBridge",
            ProviderUrl = providerUrl,
            CacheAge = CacheAgeSeconds,
            ThumbnailUrl = image,
            ThumbnailWidth = !string.IsNullOrWhiteSpace( image ) ? DefaultThumbnailSize : null,
            ThumbnailHeight = !string.IsNullOrWhiteSpace( image ) ? DefaultThumbnailSize : null,
            Html = html,
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// Builds the iframe HTML for embedding the card.
    /// </summary>
    /// <param name="embedUrl">URL for the iframe src.</param>
    /// <param name="cardUrl">URL for the fallback link.</param>
    /// <param name="title">Title for accessibility.</param>
    /// <param name="width">Iframe width.</param>
    /// <param name="height">Iframe height.</param>
    /// <returns>HTML string containing the iframe embed code.</returns>
    private static string BuildIframeHtml(
        string embedUrl,
        string cardUrl,
        string title,
        int width,
        int height
    ) {
        // HTML-encode the title for safe use in attributes
        string encodedTitle = System.Net.WebUtility.HtmlEncode( title );
        string encodedEmbedUrl = System.Net.WebUtility.HtmlEncode( embedUrl );
        string encodedCardUrl = System.Net.WebUtility.HtmlEncode( cardUrl );

        return $"""
            <iframe src="{encodedEmbedUrl}" width="{width}" height="{height}" title="{encodedTitle}" loading="lazy" role="application" aria-label="{encodedTitle}" style="border:none;overflow:hidden;background:transparent;"></iframe>
            <p><a href="{encodedCardUrl}" target="_blank">{encodedTitle}</a> - via TuneBridge</p>
            """;
    }
}
