using System.Net.Http.Json;
using System.Text.Json;

namespace BridgeBeats.Tests;

/// <summary>
/// Test helper for issuing antiforgery-protected requests against the test web host. Fetches a CSRF
/// token from the application's token endpoint and attaches it to POST requests so integration and
/// end-to-end tests can exercise endpoints guarded by antiforgery validation.
/// </summary>
public static class AntiforgeryTestHelper {
    /// <summary>
    /// Requests an antiforgery token from the application's <c>/account/antiforgery-token</c> endpoint.
    /// </summary>
    /// <param name="client">The HTTP client connected to the test web host.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The antiforgery token string returned by the endpoint.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an empty token.</exception>
    public static async Task<string> GetAntiforgeryTokenAsync( HttpClient client, CancellationToken cancellationToken = default ) {
        HttpResponseMessage tokenResponse = await client.GetAsync( "/account/antiforgery-token", cancellationToken );
        _ = tokenResponse.EnsureSuccessStatusCode( );

        JsonElement tokenResult = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>( cancellationToken );
        string? token = tokenResult.GetProperty( "token" ).GetString( );

        return string.IsNullOrEmpty( token ) ? throw new InvalidOperationException( "Failed to obtain antiforgery token" ) : token;
    }

    /// <summary>
    /// Sends a POST request with the supplied content and attaches the antiforgery token in the
    /// <c>X-XSRF-TOKEN</c> header so the request passes antiforgery validation.
    /// </summary>
    /// <param name="client">The HTTP client connected to the test web host.</param>
    /// <param name="requestUri">The relative URI to POST to.</param>
    /// <param name="content">The request body content.</param>
    /// <param name="antiforgeryToken">The token obtained from <see cref="GetAntiforgeryTokenAsync"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The HTTP response from the test host.</returns>
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
