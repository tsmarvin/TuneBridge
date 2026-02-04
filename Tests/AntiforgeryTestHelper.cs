using System.Net.Http.Json;
using System.Text.Json;

namespace BridgeBeats.Tests;

/// <summary>
/// Provides helper methods for handling antiforgery tokens in integration tests.
/// </summary>
public static class AntiforgeryTestHelper {
    /// <summary>
    /// Gets an antiforgery token from the server for use in POST requests.
    /// </summary>
    /// <param name="client">The HTTP client to use for the request.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The antiforgery token string.</returns>
    /// <exception cref="Exception">Thrown if the token cannot be obtained.</exception>
    public static async Task<string> GetAntiforgeryTokenAsync( HttpClient client, CancellationToken cancellationToken = default ) {
        HttpResponseMessage tokenResponse = await client.GetAsync( "/account/antiforgery-token", cancellationToken );
        _ = tokenResponse.EnsureSuccessStatusCode( );

        JsonElement tokenResult = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>( cancellationToken );
        string? token = tokenResult.GetProperty( "token" ).GetString( );

        return string.IsNullOrEmpty( token ) ? throw new Exception( "Failed to obtain antiforgery token" ) : token;
    }

    /// <summary>
    /// Sends a POST request with the antiforgery token header.
    /// </summary>
    /// <param name="client">The HTTP client to use for the request.</param>
    /// <param name="requestUri">The URI to POST to.</param>
    /// <param name="content">The content to send.</param>
    /// <param name="antiforgeryToken">The antiforgery token to include in the header.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The HTTP response message.</returns>
    public static async Task<HttpResponseMessage> PostWithAntiforgeryAsync(
        HttpClient client,
        string requestUri,
        HttpContent content,
        string antiforgeryToken,
        CancellationToken cancellationToken = default
    ) {
        HttpRequestMessage request = new( HttpMethod.Post, requestUri ) {
            Content = content
        };
        request.Headers.Add( "X-XSRF-TOKEN", antiforgeryToken );

        return await client.SendAsync( request, cancellationToken );
    }
}
