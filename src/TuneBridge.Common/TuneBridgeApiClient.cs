using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TuneBridge.Common.Contracts.DTOs;

namespace TuneBridge.Common;

/// <summary>
/// Client for interacting with the TuneBridge API to convert music links between providers.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TuneBridgeApiClient"/> class.
/// </remarks>
/// <param name="httpClient">HTTP client for making API requests.</param>
/// <param name="logger">Logger for diagnostic information.</param>
public class TuneBridgeApiClient(
    HttpClient httpClient,
    ILogger<TuneBridgeApiClient> logger
) {

    /// <summary>
    /// Submits a music URL to TuneBridge for cross-platform lookup.
    /// </summary>
    /// <param name="url">The music URL to look up (Spotify, Apple Music, or Tidal).</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The streaming results containing links for all available providers.</returns>
    public async IAsyncEnumerable<MediaLinkResult> SubmitUrlAsync(
        string url,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        Stream? stream = await GetResponseStreamAsync( url, cancellationToken );

        if (stream == null) {
            yield break;
        }

        await foreach (MediaLinkResult result in DeserializeResultsAsync( stream, url, cancellationToken )) {
            yield return result;
        }
    }

    /// <summary>
    /// Gets the response stream from the TuneBridge API.
    /// </summary>
    private async Task<Stream?> GetResponseStreamAsync( string url, CancellationToken cancellationToken ) {
        try {
            logger.LogDebug( "Submitting URL to TuneBridge: {url}", url );

            UrlRequest request = new( url );
            HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                "/music/lookup/url",
                request,
                cancellationToken
            );

            if (!response.IsSuccessStatusCode) {
                logger.LogWarning(
                    "TuneBridge API returned {statusCode} for URL: {url}",
                    response.StatusCode,
                    url
                );
                return null;
            }

            return await response.Content.ReadAsStreamAsync( cancellationToken );
        } catch (Exception ex) {
            logger.LogError( ex, "Failed to get response from TuneBridge for URL: {url}", url );
            return null;
        }
    }

    /// <summary>
    /// Deserializes the results from the response stream.
    /// </summary>
    private async IAsyncEnumerable<MediaLinkResult> DeserializeResultsAsync(
        Stream stream,
        string url,
        [EnumeratorCancellation] CancellationToken cancellationToken
    ) {
        await foreach (MediaLinkResult? result in JsonSerializer.DeserializeAsyncEnumerable<MediaLinkResult>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken
        )) {
            if (result != null) {
                logger.LogInformation(
                    "Received result for URL {url} with {count} messages",
                    url,
                    result.Messages?.Count ?? 0
                );
                yield return result;
            }
        }

        stream.Dispose( );
    }

    /// <summary>
    /// Request model for the TuneBridge URL lookup endpoint.
    /// </summary>
    /// <param name="Uri">The music URL to look up.</param>
    private record UrlRequest( string Uri );
}
