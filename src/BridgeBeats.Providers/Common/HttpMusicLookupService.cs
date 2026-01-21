using System.Net.Http.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.DTOs.WorkerApi;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Providers.Common;

/// <summary>
/// HTTP-based implementation of <see cref="IMusicLookupService"/> that delegates to a worker service
/// via HTTP calls. Used by the main web application to communicate with provider-specific workers.
/// </summary>
/// <param name="provider">The music provider this service handles.</param>
/// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
/// <param name="httpClientName">The named HTTP client to use (configured with service discovery).</param>
/// <param name="logger">Logger for diagnostic information.</param>
public class HttpMusicLookupService(
    SupportedProviders provider,
    IHttpClientFactory httpClientFactory,
    string httpClientName,
    ILogger<HttpMusicLookupService> logger
) : IMusicLookupService {

    /// <summary>
    /// The music provider that this HTTP adapter communicates with.
    /// </summary>
    public SupportedProviders Provider => provider;

    /// <inheritdoc/>
    public async Task<MusicLookupResult?> GetInfoAsync( string uri ) {
        LookupByUrlRequest request = new( uri );
        return await PostAsync( "/lookup/url", request );
    }

    /// <inheritdoc/>
    public async Task<MusicLookupResult?> GetInfoByISRCAsync( string isrc ) {
        LookupByIsrcRequest request = new( isrc );
        return await PostAsync( "/lookup/isrc", request );
    }

    /// <inheritdoc/>
    public async Task<MusicLookupResult?> GetInfoByUPCAsync( string upc ) {
        LookupByUpcRequest request = new( upc );
        return await PostAsync( "/lookup/upc", request );
    }

    /// <inheritdoc/>
    public async Task<MusicLookupResult?> GetInfoByIDAsync( string providerId, bool isAlbum ) {
        LookupByIdRequest request = new( providerId, isAlbum );
        return await PostAsync( "/lookup/id", request );
    }

    /// <inheritdoc/>
    public async Task<MusicLookupResult?> GetInfoAsync( string title, string artist ) {
        LookupByMetadataRequest request = new( title, artist );
        return await PostAsync( "/lookup/metadata", request );
    }

    /// <inheritdoc/>
    public async Task<MusicLookupResult?> GetInfoAsync( MusicLookupResult lookup ) {
        LookupFromResultRequest request = new(
            lookup.Artist,
            lookup.Title,
            string.IsNullOrWhiteSpace( lookup.ExternalId ) ? null : lookup.ExternalId,
            lookup.IsAlbum
        );
        return await PostAsync( "/lookup/from-result", request );
    }

    private async Task<MusicLookupResult?> PostAsync<TRequest>( string endpoint, TRequest request ) {
        try {
            using HttpClient client = httpClientFactory.CreateClient( httpClientName );

            HttpResponseMessage response = await client.PostAsJsonAsync( endpoint, request );

            if (!response.IsSuccessStatusCode) {
                logger.LogWarning(
                    "Worker {Provider} returned HTTP {StatusCode} for {Endpoint}",
                    provider,
                    (int)response.StatusCode,
                    endpoint
                );
                return null;
            }

            ProviderLookupResponse? result = await response.Content.ReadFromJsonAsync<ProviderLookupResponse>( );

            if (result == null) {
                logger.LogWarning(
                    "Worker {Provider} returned null response for {Endpoint}",
                    provider,
                    endpoint
                );
                return null;
            }

            if (!result.Success && !string.IsNullOrWhiteSpace( result.ErrorMessage )) {
                logger.LogWarning(
                    "Worker {Provider} returned error for {Endpoint}: {ErrorMessage}",
                    provider,
                    endpoint,
                    result.ErrorMessage
                );
                return null;
            }

            return result.Result;
        } catch (HttpRequestException ex) {
            logger.LogError(
                ex,
                "HTTP error communicating with {Provider} worker at {Endpoint}",
                provider,
                endpoint
            );
            throw; // Re-throw to let the caller handle worker unavailability
        } catch (Exception ex) {
            logger.LogError(
                ex,
                "Unexpected error communicating with {Provider} worker at {Endpoint}",
                provider,
                endpoint
            );
            throw;
        }
    }
}
