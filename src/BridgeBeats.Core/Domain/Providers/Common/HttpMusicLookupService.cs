using System.Net.Http.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records.WorkerApi;
using BridgeBeats.Core.Infrastructure.Logging;

namespace BridgeBeats.Core.Domain.Providers.Common;

/// <summary>
/// HTTP-based implementation of <see cref="IMusicLookupService"/> that delegates to a worker service
/// via HTTP calls. Used by the main web application to communicate with provider-specific workers.
/// </summary>
/// <param name="provider">The music provider this service handles.</param>
/// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
/// <param name="httpClientName">The named HTTP client to use (configured with service discovery).</param>
/// <param name="logger">Logger for diagnostic information.</param>
public partial class HttpMusicLookupService(
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
                LogWorkerHttpError( logger, provider, (int)response.StatusCode, endpoint );
                return null;
            }

            ProviderLookupResponse? result = await response.Content.ReadFromJsonAsync<ProviderLookupResponse>( );

            if (result == null) {
                LogWorkerNullResponse( logger, provider, endpoint );
                return null;
            }

            if (!result.Success && !string.IsNullOrWhiteSpace( result.ErrorMessage )) {
                LogWorkerError( logger, provider, endpoint, result.ErrorMessage );
                return null;
            }

            return result.Result;
        } catch (HttpRequestException ex) {
            LogHttpRequestError( logger, ex, provider, endpoint );
            throw; // Re-throw to let the caller handle worker unavailability
        } catch (Exception ex) {
            LogUnexpectedError( logger, ex, provider, endpoint );
            throw;
        }
    }

    #region LoggerMessage Methods

    /// <summary>
    /// Logs when a worker returns an HTTP error status.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Providers.Common.WorkerHttpError,
        Level = LogLevel.Warning,
        Message = "Worker {Provider} returned HTTP {StatusCode} for {Endpoint}" )]
    internal static partial void LogWorkerHttpError( ILogger logger, SupportedProviders provider, int statusCode, string endpoint );

    /// <summary>
    /// Logs when a worker returns a null response.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Providers.Common.WorkerNullResponse,
        Level = LogLevel.Warning,
        Message = "Worker {Provider} returned null response for {Endpoint}" )]
    internal static partial void LogWorkerNullResponse( ILogger logger, SupportedProviders provider, string endpoint );

    /// <summary>
    /// Logs when a worker returns an error message.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Providers.Common.WorkerError,
        Level = LogLevel.Warning,
        Message = "Worker {Provider} returned error for {Endpoint}: {ErrorMessage}" )]
    internal static partial void LogWorkerError( ILogger logger, SupportedProviders provider, string endpoint, string errorMessage );

    /// <summary>
    /// Logs an HTTP request exception when communicating with a worker.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Providers.Common.HttpRequestError,
        Level = LogLevel.Error,
        Message = "HTTP error communicating with {Provider} worker at {Endpoint}" )]
    internal static partial void LogHttpRequestError( ILogger logger, Exception ex, SupportedProviders provider, string endpoint );

    /// <summary>
    /// Logs an unexpected exception when communicating with a worker.
    /// </summary>
    [LoggerMessage(
        EventId = LogEventIds.Providers.Common.UnexpectedError,
        Level = LogLevel.Error,
        Message = "Unexpected error communicating with {Provider} worker at {Endpoint}" )]
    internal static partial void LogUnexpectedError( ILogger logger, Exception ex, SupportedProviders provider, string endpoint );

    #endregion LoggerMessage Methods
}
