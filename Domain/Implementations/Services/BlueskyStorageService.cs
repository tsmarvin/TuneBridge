using idunno.Bluesky;
using idunno.AtProto;
using idunno.AtProto.Repo;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Contracts.Records;
using TuneBridge.Domain.Implementations.Extensions;
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
        private readonly SemaphoreSlim _authLock = new( 1, 1 );
        private bool _isAuthenticated;

        public BlueskyStorageService(
            string pdsUrl,
            string identifier,
            string password,
            ILogger<BlueskyStorageService> logger
        ) {
            // Note: idunno.Bluesky library uses the default Bluesky PDS
            // Validate PDS URL - current library version only supports default Bluesky PDS
            if (!string.IsNullOrWhiteSpace( pdsUrl ) && pdsUrl != "https://bsky.social") {
                throw new NotSupportedException( 
                    $"Custom PDS URL '{pdsUrl}' is not supported by the current version of idunno.Bluesky library. " +
                    "Only the default Bluesky PDS (https://bsky.social) is supported. " +
                    "To use a custom PDS, either use the default value or upgrade to a library version that supports custom service URLs." );
            }
            
            _agent = new BlueskyAgent( );
            _identifier = identifier;
            _password = password;
            _logger = logger;
            
            _logger.LogInformation( "BlueskyStorageService initialized with default Bluesky PDS" );
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

        /// <inheritdoc/>
        public async Task<bool> UpdateMediaLinkResultAsync( string recordUri, MediaLinkResult result ) {
            await EnsureAuthenticatedAsync( );

            try {
                // Parse the AT-URI to get repo and rkey
                var atUri = new AtUri( recordUri );

                if (atUri.RecordKey is null) {
                    _logger.LogWarning( "Invalid AT-URI, missing record key: {uri}", recordUri );
                    return false;
                }
                
                // Convert MediaLinkResult DTO to custom record
                var record = ConvertToRecord( result );

                // Update the record on Bluesky PDS using PutRecord
                var putResult = await _agent.PutRecord(
                    record: record,
                    collection: MediaLinkResultCollection,
                    rKey: atUri.RecordKey
                );

                if (!putResult.Succeeded) {
                    string errorMsg = putResult.AtErrorDetail?.Message ?? $"HTTP {putResult.StatusCode}";
                    _logger.LogWarning( "Failed to update record on Bluesky PDS: {error}", errorMsg );
                    return false;
                }

                _logger.LogInformation( "Successfully updated MediaLinkResult on Bluesky PDS: {uri}", recordUri );
                return true;
            } catch (Exception ex) {
                _logger.LogError( ex, "Failed to update MediaLinkResult on Bluesky PDS: {uri}", recordUri );
                return false;
            }
        }

        /// <summary>
        /// Converts a MediaLinkResult DTO to a MediaLinkResultRecord for storage.
        /// Note: Input links are NOT included in the PDS record for user privacy.
        /// They are tracked only in SQLite.
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
                lookedUpAt: DateTimeOffset.UtcNow
            );
        }

        /// <summary>
        /// Converts a MediaLinkResultRecord from storage back to a MediaLinkResult DTO.
        /// Note: Input links are not stored in PDS records, only provider results.
        /// </summary>
        private static MediaLinkResult ConvertFromRecord( MediaLinkResultRecord record ) {
            var result = new MediaLinkResult( );

            foreach (var providerResult in record.Results) {
                // Try to parse provider using consistent logic, skip if unknown
                if (!TryParseProvider(providerResult.Provider, out var provider)) {
                    continue;
                }

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

            // Input links are tracked only in SQLite, not in PDS records
            return result;
        }

        /// <summary>
        /// Tries to parse a provider string into a <see cref="SupportedProviders"/> enum value.
        /// Uses the enum's Description attribute for matching to ensure consistency with display names.
        /// </summary>
        /// <param name="providerString">The provider string to parse.</param>
        /// <param name="provider">The parsed provider enum value if successful.</param>
        /// <returns>True if the provider was successfully parsed, false otherwise.</returns>
        private static bool TryParseProvider(string providerString, out SupportedProviders provider) {
            provider = default;
            
            if (string.IsNullOrWhiteSpace(providerString)) {
                return false;
            }

            // Try parsing by enum name first (case-insensitive)
            if (Enum.TryParse<SupportedProviders>(providerString, true, out provider)) {
                return true;
            }

            // Try matching against Description attributes
            foreach (SupportedProviders p in Enum.GetValues<SupportedProviders>()) {
                var description = p.GetDescription();
                if (string.Equals(description, providerString, StringComparison.OrdinalIgnoreCase)) {
                    provider = p;
                    return true;
                }
                
                // Also try description without spaces (e.g., "AppleMusic" vs "Apple Music")
                var descriptionNoSpaces = description.Replace(" ", "", StringComparison.Ordinal);
                if (string.Equals(descriptionNoSpaces, providerString, StringComparison.OrdinalIgnoreCase)) {
                    provider = p;
                    return true;
                }
            }

            return false;
        }
    }
}
