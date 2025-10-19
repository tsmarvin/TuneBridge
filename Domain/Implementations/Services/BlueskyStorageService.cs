using System.Text.Json;
using idunno.Bluesky;
using idunno.AtProto;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Implementation of <see cref="IBlueskyStorageService"/> that stores MediaLinkResult records on Bluesky PDS
    /// as custom records using the AT Protocol.
    /// </summary>
    /// <remarks>
    /// This service uses the idunno.Bluesky library to interact with Bluesky PDS.
    /// MediaLinkResults are stored as JSON in post text.
    /// </remarks>
    public class BlueskyStorageService : IBlueskyStorageService {

        private readonly BlueskyAgent _agent;
        private readonly string _identifier;
        private readonly string _password;
        private readonly ILogger<BlueskyStorageService> _logger;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly SemaphoreSlim _authLock = new( 1, 1 );
        private bool _isAuthenticated;

        public BlueskyStorageService(
            string pdsUrl,
            string identifier,
            string password,
            ILogger<BlueskyStorageService> logger,
            JsonSerializerOptions serializerOptions
        ) {
            // If a custom PDS URL is provided (not default bsky.social), we need to handle it differently
            // For now, we'll use the default Bluesky service
            _agent = new BlueskyAgent( );
            _identifier = identifier;
            _password = password;
            _logger = logger;
            _serializerOptions = serializerOptions;
            
            _logger.LogInformation( "BlueskyStorageService initialized with PDS URL: {url}", pdsUrl );
        }

        /// <summary>
        /// Ensures the agent is authenticated with Bluesky PDS.
        /// </summary>
        private async Task EnsureAuthenticatedAsync( ) {
            if (_isAuthenticated) {
                return;
            }

            await _authLock.WaitAsync( );
            try {
                if (_isAuthenticated) {
                    return;
                }

                var loginResult = await _agent.Login( _identifier, _password );
                if (!loginResult.Succeeded) {
                    string errorMsg = loginResult.AtErrorDetail?.Message ?? $"HTTP {loginResult.StatusCode}";
                    throw new InvalidOperationException( $"Failed to authenticate with Bluesky PDS: {errorMsg}" );
                }

                _isAuthenticated = true;
                _logger.LogInformation( "Successfully authenticated with Bluesky PDS" );
            } finally {
                _ = _authLock.Release( );
            }
        }

        /// <inheritdoc/>
        public async Task<string> StoreMediaLinkResultAsync( MediaLinkResult result ) {
            await EnsureAuthenticatedAsync( );

            try {
                // Serialize the MediaLinkResult to JSON
                string json = JsonSerializer.Serialize( result, _serializerOptions );

                // Create a post with the JSON as text
                // Add a marker prefix to identify this as a TuneBridge MediaLinkResult
                string postText = $"#TuneBridge MediaLinkResult\n{json}";

                var postResult = await _agent.Post( postText );

                if (!postResult.Succeeded || postResult.Result is null) {
                    string errorMsg = postResult.AtErrorDetail?.Message ?? $"HTTP {postResult.StatusCode}";
                    throw new InvalidOperationException( $"Failed to create post on Bluesky PDS: {errorMsg}" );
                }

                _logger.LogInformation( "Successfully stored MediaLinkResult on Bluesky PDS: {uri}", postResult.Result.Uri );

                return postResult.Result.Uri.ToString( );
            } catch (Exception ex) {
                _logger.LogError( ex, "Failed to store MediaLinkResult on Bluesky PDS" );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri ) {
            await EnsureAuthenticatedAsync( );

            try {
                // Parse the AT-URI
                var atUri = new AtUri( recordUri );
                
                // Get the post from Bluesky PDS
                var getPostResult = await _agent.GetPost( atUri );

                if (!getPostResult.Succeeded || getPostResult.Result?.Record?.Text is null) {
                    _logger.LogWarning( "Record not found or has no text: {uri}", recordUri );
                    return null;
                }

                string text = getPostResult.Result.Record.Text;

                // Remove the marker prefix if present
                if (text.StartsWith( "#TuneBridge MediaLinkResult\n" )) {
                    text = text["#TuneBridge MediaLinkResult\n".Length..];
                }

                // Deserialize the JSON
                var result = JsonSerializer.Deserialize<MediaLinkResult>( text, _serializerOptions );

                return result;
            } catch (Exception ex) {
                _logger.LogError( ex, "Failed to retrieve MediaLinkResult from Bluesky PDS: {uri}", recordUri );
                return null;
            }
        }
    }
}
