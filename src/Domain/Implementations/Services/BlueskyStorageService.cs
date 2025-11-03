using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
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
    /// MediaLinkResults are stored as custom media.tunebridge.dev.lookup.result lexicon records.
    /// </remarks>
    public class BlueskyStorageService : IBlueskyStorageService {

        /// <summary>
        /// The NSID (Namespaced Identifier) for the TuneBridge MediaLinkResult lexicon.
        /// </summary>
        private static readonly Nsid MediaLinkResultCollection = new( "media.tunebridge.dev.lookup.result" );

        /// <summary>
        /// Static cached dictionary mapping provider strings to SupportedProviders enum values.
        /// Initialized once at startup for O(1) lookups.
        /// </summary>
        private static readonly Lazy<Dictionary<string, SupportedProviders>> _providerStringToEnum =
            new(CreateProviderMappings);

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

                AtProtoHttpResult<bool> loginResult = await _agent.Login( _identifier, _password );
                if (!loginResult.Succeeded) {
                    string errorMsg = loginResult.AtErrorDetail?.Message ?? $"HTTP {loginResult.StatusCode}";
                    throw new InvalidOperationException( $"Failed to authenticate to PDS: {errorMsg}" );
                }

                _isAuthenticated = true;
                _logger.LogInformation( "Successfully authenticated to PDS" );
            } finally {
                _ = _authLock.Release( );
            }
        }

        /// <inheritdoc/>
        public async Task<string> StoreMediaLinkResultAsync( MediaLinkResult result ) {
            await EnsureAuthenticatedAsync( );

            try {
                // Convert MediaLinkResult DTO to custom record
                MediaLinkResultRecord record = ConvertToRecord( result );

                // Create the custom record on Bluesky PDS
                AtProtoHttpResult<CreateRecordResult> createResult = await _agent.CreateRecord(
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
                AtUri atUri = new( recordUri );

                // Get the record from Bluesky PDS
                AtProtoHttpResult<AtProtoRepositoryRecord<MediaLinkResultRecord>> getRecordResult = await _agent.GetRecord<MediaLinkResultRecord>(
                    uri: atUri,
                    cid: null
                );

                if (!getRecordResult.Succeeded || getRecordResult.Result?.Value is null) {
                    _logger.LogWarning( "Record not found: {uri}", recordUri );
                    return null;
                }

                // Convert the custom record back to MediaLinkResult DTO
                MediaLinkResult mediaLinkResult = ConvertFromRecord( getRecordResult.Result.Value );

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
                AtUri atUri = new( recordUri );

                if (atUri.RecordKey is null) {
                    _logger.LogWarning( "Invalid AT-URI, missing record key: {uri}", recordUri );
                    return false;
                }

                // Convert MediaLinkResult DTO to custom record
                MediaLinkResultRecord record = ConvertToRecord( result );

                // Update the record on Bluesky PDS using PutRecord
                AtProtoHttpResult<PutRecordResult> putResult = await _agent.PutRecord(
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
            List<ProviderResultRecord> providerResults = new( );

            foreach ((SupportedProviders provider, MusicLookupResultDto lookupResult) in result.Results) {
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
            MediaLinkResult result = new( );

            foreach (ProviderResultRecord providerResult in record.Results) {
                // Try to parse provider using consistent logic, skip if unknown
                if (!TryParseProvider( providerResult.Provider, out SupportedProviders provider )) {
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
        /// Creates a static dictionary mapping all possible provider string representations to their enum values.
        /// Includes enum names, Description attribute values, and description values without spaces.
        /// </summary>
        /// <returns>A dictionary with case-insensitive string keys mapped to SupportedProviders enum values.</returns>
        private static Dictionary<string, SupportedProviders> CreateProviderMappings( ) {
            Dictionary<string, SupportedProviders> dict = new( StringComparer.OrdinalIgnoreCase );

            foreach (SupportedProviders p in Enum.GetValues<SupportedProviders>( )) {
                // Add enum name (e.g., "Spotify")
                dict[p.ToString( )] = p;

                // Add description (e.g., "Apple Music")
                string description = p.GetDescription();
                dict[description] = p;

                // Add description without spaces (e.g., "AppleMusic")
                string descriptionNoSpaces = description.Replace(" ", "", StringComparison.Ordinal);
                if (descriptionNoSpaces != description) {
                    dict[descriptionNoSpaces] = p;
                }
            }

            return dict;
        }

        /// <summary>
        /// Tries to parse a provider string into a <see cref="SupportedProviders"/> enum value.
        /// Uses a cached dictionary for O(1) lookup performance.
        /// </summary>
        /// <param name="providerString">The provider string to parse.</param>
        /// <param name="provider">The parsed provider enum value if successful.</param>
        /// <returns>True if the provider was successfully parsed, false otherwise.</returns>
        private static bool TryParseProvider( string providerString, out SupportedProviders provider ) {
            provider = default;

            return !string.IsNullOrWhiteSpace( providerString ) && _providerStringToEnum.Value.TryGetValue( providerString, out provider );
        }
    }
}
