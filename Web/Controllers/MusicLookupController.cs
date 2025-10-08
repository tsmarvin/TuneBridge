using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// REST API endpoints for cross-platform music lookup and link translation. These endpoints enable
/// clients to convert music links between services (Spotify ↔ Apple Music) or search for tracks/albums
/// by metadata. Used by the web interface and can be consumed by third-party integrations.
/// </summary>
/// <param name="svc">Injected service that handles provider queries and result aggregation.</param>
[ApiController]
[Route( "music/lookup" )]
public class MusicLookupController( IMediaLinkService svc ) : ControllerBase {

    private readonly IMediaLinkService _svc = svc;

    /// <summary>Request containing one or more music URLs to parse and look up across platforms.</summary>
    /// <param name="Uri">Space-separated or newline-separated URLs from supported services (Spotify, Apple Music).</param>
    public record UrlReq( string Uri );

    /// <summary>Request containing an ISRC code for exact track lookup.</summary>
    /// <param name="Isrc">12-character ISRC (International Standard Recording Code), e.g., "USRC17607839".</param>
    public record IsrcReq( string Isrc );

    /// <summary>Request containing a UPC code for exact album lookup.</summary>
    /// <param name="Upc">UPC barcode number (12-13 digits), e.g., "00602537518357".</param>
    public record UpcReq( string Upc );

    /// <summary>Request containing track/album search criteria.</summary>
    /// <param name="Title">Track or album title. Supports partial matches and common variations.</param>
    /// <param name="Artist">Primary artist name. Should match the main credited artist for best results.</param>
    public record TitleReq( string Title, string Artist );

    /// <summary>
    /// Batch endpoint: Parses multiple music URLs from text and returns all results in a single response.
    /// Useful for processing Discord/Slack messages or web pages containing multiple music links.
    /// Results are deduplicated if multiple URLs point to the same content.
    /// </summary>
    /// <param name="req">Request body with text containing music URLs to extract and process.</param>
    /// <returns>
    /// HTTP 200 with JSON array of <see cref="MediaLinkResult"/> objects. Each result contains URLs
    /// from all providers where the track/album was found. Returns empty array if no valid URLs found.
    /// </returns>
    /// <response code="200">Successfully parsed and looked up all URLs.</response>
    /// <response code="400">Invalid request body or malformed URLs.</response>
    [HttpPost( "urlList" )]
    public async Task<IActionResult> ByUrlList( [FromBody] UrlReq req ) {
        List<MediaLinkResult> results = [];
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            results.Add( result );
        }

        return Ok( results );
    }

    /// <summary>
    /// Streaming endpoint: Parses music URLs and yields results progressively as they're discovered.
    /// Better for real-time applications or when processing large amounts of content, as it starts
    /// returning data immediately without waiting for all lookups to complete.
    /// </summary>
    /// <param name="req">Request body with text containing music URLs to extract and process.</param>
    /// <returns>
    /// Server-Sent Events stream of <see cref="MediaLinkResult"/> objects. Each result is yielded
    /// as soon as it's available, enabling progressive UI updates. Stream completes when all URLs
    /// have been processed.
    /// </returns>
    /// <remarks>
    /// Clients should use streaming JSON parsers or SSE libraries to consume this endpoint efficiently.
    /// The response uses chunked transfer encoding.
    /// </remarks>
    [HttpPost( "url" )]
    public async IAsyncEnumerable<MediaLinkResult> ByUrl( [FromBody] UrlReq req ) {
        await foreach (MediaLinkResult result in _svc.GetInfoAsync( req.Uri )) {
            yield return result;
        }
    }

    /// <summary>
    /// Performs an exact track lookup using ISRC (International Standard Recording Code). This is the
    /// most reliable method for finding tracks across platforms as ISRCs are globally standardized and
    /// consistent. Recommended over title/artist search when the ISRC is known.
    /// </summary>
    /// <param name="req">Request containing the 12-character ISRC code (hyphens optional).</param>
    /// <returns>
    /// HTTP 200 with <see cref="MediaLinkResult"/> containing provider URLs if the track was found,
    /// or HTTP 200 with null if the ISRC doesn't exist in any provider's catalog.
    /// </returns>
    /// <response code="200">Lookup completed (result may be null if ISRC not found).</response>
    /// <response code="400">Invalid ISRC format in request body.</response>
    /// <example>
    /// POST /music/lookup/isrc
    /// { "isrc": "USRC17607839" }
    /// </example>
    [HttpPost( "isrc" )]
    public async Task<IActionResult> ByIsrc( [FromBody] IsrcReq req )
        => Ok( await _svc.GetInfoByISRCAsync( req.Isrc ) );

    /// <summary>
    /// Performs an exact album lookup using UPC (Universal Product Code). Particularly useful for
    /// distinguishing between different editions of the same album (standard vs deluxe, regional variants).
    /// More reliable than title search for albums with complex naming or special characters.
    /// </summary>
    /// <param name="req">Request containing the UPC barcode number (typically 12-13 digits).</param>
    /// <returns>
    /// HTTP 200 with <see cref="MediaLinkResult"/> containing provider URLs if the album was found,
    /// or HTTP 200 with null if the UPC doesn't exist in any provider's catalog.
    /// </returns>
    /// <response code="200">Lookup completed (result may be null if UPC not found).</response>
    /// <response code="400">Invalid UPC format in request body.</response>
    /// <example>
    /// POST /music/lookup/upc
    /// { "upc": "00602537518357" }
    /// </example>
    [HttpPost( "upc" )]
    public async Task<IActionResult> ByUpc( [FromBody] UpcReq req )
        => Ok( await _svc.GetInfoByUPCAsync( req.Upc ) );

    /// <summary>
    /// Searches for tracks or albums by title and artist name across all configured providers.
    /// Uses fuzzy matching to handle common title variations, typos, and formatting differences.
    /// May return no results if the search is too broad or the content isn't available on configured platforms.
    /// </summary>
    /// <param name="req">Request containing the title and artist to search for.</param>
    /// <returns>
    /// HTTP 200 with <see cref="MediaLinkResult"/> containing provider URLs if matches were found,
    /// or HTTP 200 with null if no matches were found on any platform.
    /// </returns>
    /// <response code="200">Search completed (result may be null if no matches found).</response>
    /// <response code="400">Missing or invalid title/artist in request body.</response>
    /// <remarks>
    /// For best results, provide the primary artist name and official title. The search handles common
    /// variations like "(Deluxe)", "- Single", "Remastered", etc. automatically.
    /// </remarks>
    /// <example>
    /// POST /music/lookup/title
    /// { "title": "Shake It Off", "artist": "Taylor Swift" }
    /// </example>
    [HttpPost( "title" )]
    public async Task<IActionResult> ByTitle( [FromBody] TitleReq req )
        => Ok( await _svc.GetInfoAsync( req.Title, req.Artist ) );
}
