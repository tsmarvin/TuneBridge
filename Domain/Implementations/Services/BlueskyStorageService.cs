using System.Text.Json;
using idunno.Bluesky;
using idunno.AtProto;
using idunno.AtProto.Repo;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Records;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Implementation of <see cref="IBlueskyStorageService"/> that stores MediaLinkResult records on Bluesky PDS
    /// as custom lexicon records using the AT Protocol.
    /// </summary>
    /// <remarks>
    /// This service uses the idunno.Bluesky library to interact with Bluesky PDS.
    /// MediaLinkResults are stored as custom media.tunebridge.lookup.result lexicon records.
    /// </remarks>
    public class BlueskyStorageService : IBlueskyStorageService {

        /// <summary>
        /// The NSID (Namespaced Identifier) for the TuneBridge MediaLinkResult lexicon.
        /// </summary>
        private static readonly Nsid MediaLinkResultCollection = new( "media.tunebridge.lookup.result" );

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
                // Convert MediaLinkResult DTO to custom record
                var record = ConvertToRecord( result );

                // Create the custom record on Bluesky PDS
                var createResult = await _agent.CreateRecord(
                    record: record,
                    collection: MediaLinkResultCollection
                );

                if (!createResult.Succeeded || createResult.Result is null) {
                    string errorMsg = createResult.AtErrorDetail?.Message ?? $"HTTP {createResult.StatusCode}";
                    throw new InvalidOperationException( $"Failed to create record on Bluesky PDS: {errorMsg}" );
                }

                _logger.LogInformation( "Successfully stored MediaLinkResult on Bluesky PDS: {uri}", createResult.Result.Uri );

                return createResult.Result.Uri.ToString( );
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
                
                // Get the record from Bluesky PDS
                var getRecordResult = await _agent.GetRecord<MediaLinkResultRecord>(
                    uri: atUri,
                    cid: null
                );

                if (!getRecordResult.Succeeded || getRecordResult.Result?.Value is null) {
                    _logger.LogWarning( "Record not found: {uri}", recordUri );
                    return null;
                }

                // Convert the custom record back to MediaLinkResult DTO
                var mediaLinkResult = ConvertFromRecord( getRecordResult.Result.Value );

                return mediaLinkResult;
            } catch (Exception ex) {
                _logger.LogError( ex, "Failed to retrieve MediaLinkResult from Bluesky PDS: {uri}", recordUri );
                return null;
            }
        }

        /// <summary>
        /// Converts a MediaLinkResult DTO to a MediaLinkResultRecord for storage.
        /// </summary>
        private static MediaLinkResultRecord ConvertToRecord( MediaLinkResult result ) {
            var providerResults = new List<ProviderResultRecord>( );

            foreach (var (provider, lookupResult) in result.Results) {
                string providerName = provider switch {
                    SupportedProviders.AppleMusic => "appleMusic",
                    SupportedProviders.Spotify => "spotify",
                    SupportedProviders.Tidal => "tidal",
                    _ => provider.ToString( ).ToLowerInvariant( )
                };

                providerResults.Add( new ProviderResultRecord(
                    provider: providerName,
                    artist: lookupResult.Artist,
                    title: lookupResult.Title,
                    url: lookupResult.URL,
                    marketRegion: lookupResult.MarketRegion,
                    externalId: string.IsNullOrEmpty( lookupResult.ExternalId ) ? null : lookupResult.ExternalId,
                    artUrl: string.IsNullOrEmpty( lookupResult.ArtUrl ) ? null : lookupResult.ArtUrl,
                    isAlbum: lookupResult.IsAlbum
                ) );
            }

            return new MediaLinkResultRecord(
                results: providerResults,
                lookedUpAt: DateTimeOffset.UtcNow,
                inputLinks: result._inputLinks.Count > 0 ? result._inputLinks : null
            );
        }

        /// <summary>
        /// Converts a MediaLinkResultRecord from storage back to a MediaLinkResult DTO.
        /// </summary>
        private static MediaLinkResult ConvertFromRecord( MediaLinkResultRecord record ) {
            var result = new MediaLinkResult( );

            foreach (var providerResult in record.Results) {
                SupportedProviders provider = providerResult.Provider.ToLowerInvariant( ) switch {
                    "applemusic" => SupportedProviders.AppleMusic,
                    "spotify" => SupportedProviders.Spotify,
                    "tidal" => SupportedProviders.Tidal,
                    _ => Enum.TryParse<SupportedProviders>( providerResult.Provider, true, out var p ) ? p : SupportedProviders.Spotify
                };

                result.Results.Add( provider, new MusicLookupResultDto {
                    Artist = providerResult.Artist,
                    Title = providerResult.Title,
                    ExternalId = providerResult.ExternalId ?? string.Empty,
                    URL = providerResult.Url,
                    ArtUrl = providerResult.ArtUrl ?? string.Empty,
                    MarketRegion = providerResult.MarketRegion,
                    IsAlbum = providerResult.IsAlbum
                } );
            }

            if (record.InputLinks is not null) {
                result._inputLinks.AddRange( record.InputLinks );
            }

            return result;
        }
    }
}
