using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Worker.Discord.Logging;
using Polly.Timeout;

namespace BridgeBeats.Worker.Discord.Services {

    /// <summary>
    /// HTTP client for the BridgeBeats Web API, used by the Discord worker to resolve music links and
    /// to store shareable cards. This is a plain internal client-to-server call authenticated with a
    /// shared <c>X-Service-Key</c> header, not a peer protocol. All calls are designed to swallow
    /// transport and deserialization failures: a failed lookup yields no results and a failed
    /// card-store returns <see langword="null"/> rather than throwing, so a Web outage degrades the
    /// bot gracefully instead of crashing the message handler.
    /// </summary>
    public partial class BridgeBeatsApiClient {

        /// <summary>The underlying HTTP client, configured by the host with base address, timeout, and headers.</summary>
        private readonly HttpClient _httpClient;

        /// <summary>The logger used to record API transport and deserialization failures.</summary>
        private readonly ILogger<BridgeBeatsApiClient> _logger;

        /// <summary>Case-insensitive JSON options used to deserialize Web API responses.</summary>
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes the client with its HTTP client, logger, and the card-link domain. A non-blank
        /// <paramref name="domain"/> enables the card-store path (see <see cref="IsCardServiceEnabled"/>).
        /// </summary>
        /// <param name="httpClient">The configured HTTP client targeting the BridgeBeats Web API.</param>
        /// <param name="logger">The logger for API failures.</param>
        /// <param name="domain">The domain used to build shareable card URLs; an empty value disables the card service.</param>
        public BridgeBeatsApiClient( HttpClient httpClient, ILogger<BridgeBeatsApiClient> logger, string domain ) {
            _httpClient = httpClient;
            _logger = logger;
            Domain = domain;
            _jsonOptions = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Gets a value indicating whether the card-store path is available. <see langword="true"/>
        /// when a non-blank <see cref="Domain"/> was supplied.
        /// </summary>
        public bool IsCardServiceEnabled => !string.IsNullOrWhiteSpace( Domain );

        /// <summary>
        /// Gets the domain used to build shareable card URLs. Empty when no card service is configured.
        /// </summary>
        public string Domain { get; }

        /// <summary>
        /// Resolves the music links in <paramref name="content"/> by POSTing it to the Web
        /// <c>/music/lookup/url</c> endpoint and streaming back the deserialized results. The call is
        /// failure-tolerant: a timeout or cancellation, an HTTP transport failure, or a malformed
        /// response body each ends the sequence with no further results rather than throwing, after
        /// logging the cause. An empty or null response yields nothing without logging an error.
        /// </summary>
        /// <param name="content">The message text containing one or more music links to resolve.</param>
        /// <returns>
        /// An asynchronous sequence of <see cref="MediaLinkResult"/> items, one per resolved link;
        /// empty when the lookup failed or returned no results.
        /// </returns>
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
        /// Stores a shareable card for the supplied result by POSTing it to the Web
        /// <c>/api/card/store</c> endpoint, returning the card URL from the response. Returns
        /// <see langword="null"/> when the card service is disabled (see
        /// <see cref="IsCardServiceEnabled"/>) or when the call fails at the transport or
        /// deserialization level; failures are logged rather than thrown.
        /// </summary>
        /// <param name="result">The cross-provider lookup result to persist as a card.</param>
        /// <returns>The shareable card URL, or <see langword="null"/> when unavailable.</returns>
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
        /// The response shape returned by the Web <c>/api/card/store</c> endpoint.
        /// </summary>
        /// <param name="CardUrl">The shareable card URL, or <see langword="null"/> when none was produced.</param>
        private record StoreCardResponse( string? CardUrl );

        #region LoggerMessage Methods

        /// <summary>Logs that a music-lookup API call timed out or was cancelled.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="ex">The timeout or cancellation exception.</param>
        [LoggerMessage(
            EventId = LogEventIds.LookupApiTimeout,
            Level = LogLevel.Warning,
            Message = "Music lookup API call timed out or was cancelled; yielding no results" )]
        private static partial void LogLookupApiTimeout( ILogger logger, Exception ex );

        /// <summary>Logs that a music-lookup API call failed at the HTTP transport level.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="ex">The transport exception.</param>
        [LoggerMessage(
            EventId = LogEventIds.LookupApiError,
            Level = LogLevel.Error,
            Message = "Failed to call music lookup API" )]
        private static partial void LogLookupApiError( ILogger logger, Exception ex );

        /// <summary>Logs that the music-lookup response body could not be deserialized.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="ex">The deserialization exception.</param>
        [LoggerMessage(
            EventId = LogEventIds.LookupDeserializeError,
            Level = LogLevel.Error,
            Message = "Failed to deserialize lookup response" )]
        private static partial void LogLookupDeserializeError( ILogger logger, Exception ex );

        /// <summary>Logs that a card-store API call failed at the HTTP transport level.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="ex">The transport exception.</param>
        [LoggerMessage(
            EventId = LogEventIds.StoreCardApiError,
            Level = LogLevel.Error,
            Message = "Failed to store card via API" )]
        private static partial void LogStoreCardApiError( ILogger logger, Exception ex );

        /// <summary>Logs that the card-store response body could not be deserialized.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="ex">The deserialization exception.</param>
        [LoggerMessage(
            EventId = LogEventIds.StoreCardDeserializeError,
            Level = LogLevel.Error,
            Message = "Failed to deserialize store card response" )]
        private static partial void LogStoreCardDeserializeError( ILogger logger, Exception ex );

        #endregion
    }

}
