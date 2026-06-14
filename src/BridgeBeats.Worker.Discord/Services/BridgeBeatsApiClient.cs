using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Worker.Discord.Logging;
using Polly.Timeout;

namespace BridgeBeats.Worker.Discord.Services {

    /// <summary>
    /// HTTP client for communicating with the BridgeBeats Web API.
    /// Provides methods for music link resolution and OpenGraph card storage.
    /// </summary>
    public partial class BridgeBeatsApiClient {

        private readonly HttpClient _httpClient;
        private readonly ILogger<BridgeBeatsApiClient> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="BridgeBeatsApiClient"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client configured for BridgeBeats Web API.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="domain">The base URL for OpenGraph card links.</param>
        public BridgeBeatsApiClient( HttpClient httpClient, ILogger<BridgeBeatsApiClient> logger, string domain ) {
            _httpClient = httpClient;
            _logger = logger;
            Domain = domain;
            _jsonOptions = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Gets whether the OpenGraph card service is enabled (base URL is configured).
        /// </summary>
        public bool IsCardServiceEnabled => !string.IsNullOrWhiteSpace( Domain );

        /// <summary>
        /// Gets the base URL for OpenGraph cards.
        /// </summary>
        public string Domain { get; }

        /// <summary>
        /// Resolves music links from the provided content by calling the Web API.
        /// </summary>
        /// <param name="content">The text content potentially containing music URLs.</param>
        /// <returns>An async enumerable of media link results.</returns>
        public async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
            HttpResponseMessage response;
            try {
                response = await _httpClient.PostAsJsonAsync( "/music/lookup/url", new { Uri = content } );
                _ = response.EnsureSuccessStatusCode( );
            } catch (TimeoutRejectedException ex) {
                // Polly AttemptTimeout fired — the pipeline rejected the attempt.
                LogLookupApiTimeout( _logger, ex );
                yield break;
            } catch (OperationCanceledException ex) {
                // HttpClient transport deadline or cooperative cancellation.
                // TaskCanceledException (IS-A OperationCanceledException) is caught here too.
                LogLookupApiTimeout( _logger, ex );
                yield break;
            } catch (HttpRequestException ex) {
                LogLookupApiError( _logger, ex );
                yield break;
            }

            // Parse the streaming response
            await using Stream stream = await response.Content.ReadAsStreamAsync( );

            // The endpoint returns IAsyncEnumerable<MediaLinkResult> as a JSON array
            MediaLinkResult[]? results;
            try {
                results = await JsonSerializer.DeserializeAsync<MediaLinkResult[]>( stream, _jsonOptions );
            } catch (JsonException ex) {
                LogLookupDeserializeError( _logger, ex );
                yield break;
            }

            if (results != null) {
                foreach (MediaLinkResult result in results) {
                    yield return result;
                }
            }
        }

        /// <summary>
        /// Stores a MediaLinkResult and returns the OpenGraph card URL.
        /// </summary>
        /// <param name="result">The media link result to store.</param>
        /// <returns>The full URL to the OpenGraph card, or null if storage failed.</returns>
        public async Task<string?> StoreCardAsync( MediaLinkResult result ) {
            if (!IsCardServiceEnabled) {
                return null;
            }

            try {
                HttpResponseMessage response = await _httpClient.PostAsJsonAsync( "/api/card/store", result );
                _ = response.EnsureSuccessStatusCode( );

                StoreCardResponse? storeResponse = await response.Content.ReadFromJsonAsync<StoreCardResponse>( _jsonOptions );
                return storeResponse?.CardUrl;
            } catch (HttpRequestException ex) {
                LogStoreCardApiError( _logger, ex );
                return null;
            } catch (JsonException ex) {
                LogStoreCardDeserializeError( _logger, ex );
                return null;
            }
        }

        /// <summary>
        /// Response from the card storage API endpoint.
        /// </summary>
        private record StoreCardResponse( string? CardUrl );

        #region LoggerMessage Methods

        /// <summary>Logs timeout or cancellation of the music lookup API call.</summary>
        [LoggerMessage(
            EventId = LogEventIds.LookupApiTimeout,
            Level = LogLevel.Warning,
            Message = "Music lookup API call timed out or was cancelled; yielding no results" )]
        private static partial void LogLookupApiTimeout( ILogger logger, Exception ex );

        /// <summary>Logs failure to call music lookup API.</summary>
        [LoggerMessage(
            EventId = LogEventIds.LookupApiError,
            Level = LogLevel.Error,
            Message = "Failed to call music lookup API" )]
        private static partial void LogLookupApiError( ILogger logger, Exception ex );

        /// <summary>Logs failure to deserialize lookup response.</summary>
        [LoggerMessage(
            EventId = LogEventIds.LookupDeserializeError,
            Level = LogLevel.Error,
            Message = "Failed to deserialize lookup response" )]
        private static partial void LogLookupDeserializeError( ILogger logger, Exception ex );

        /// <summary>Logs failure to store card via API.</summary>
        [LoggerMessage(
            EventId = LogEventIds.StoreCardApiError,
            Level = LogLevel.Error,
            Message = "Failed to store card via API" )]
        private static partial void LogStoreCardApiError( ILogger logger, Exception ex );

        /// <summary>Logs failure to deserialize store card response.</summary>
        [LoggerMessage(
            EventId = LogEventIds.StoreCardDeserializeError,
            Level = LogLevel.Error,
            Message = "Failed to deserialize store card response" )]
        private static partial void LogStoreCardDeserializeError( ILogger logger, Exception ex );

        #endregion
    }

}
