using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using BridgeBeats.Domain.Contracts.DTOs;
using BridgeBeats.Domain.Contracts.Records;
using BridgeBeats.Domain.Implementations.Extensions;
using BridgeBeats.Domain.Implementations.Utilities;
using BridgeBeats.Domain.Interfaces;
using BridgeBeats.Domain.Types.Enums;

namespace BridgeBeats.Domain.Implementations.Services {

    /// <summary>
    /// Implementation of <see cref="IATProtoStorageService"/> that stores MediaLinkResult records on ATProto PDS
    /// as custom lexicon records using the AT Protocol.
    /// </summary>
    /// <remarks>
    /// This service uses the idunno.Bluesky library to interact with ATProto-compatible PDS instances.
    /// MediaLinkResults are stored as custom media.tunebridge.dev.lookup lexicon records.
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the <see cref="ATProtoStorageService"/> class.
    /// </remarks>
    /// <param name="identifier">The account identifier (handle or DID).</param>
    /// <param name="password">The app password for authentication.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public class ATProtoStorageService(
        string identifier,
        string password,
        ILogger<ATProtoStorageService> logger
    ) : IATProtoStorageService {

        /// <summary>
        /// The NSID (Namespaced Identifier) for the BridgeBeats MediaLinkResult lexicon.
        /// </summary>
        private static readonly Nsid s_mediaLinkResultCollection = new( "media.tunebridge.dev.lookup" );

        /// <summary>
        /// Static cached dictionary mapping provider strings to SupportedProviders enum values.
        /// Initialized once at startup for O(1) lookups.
        /// </summary>
        private static readonly Lazy<Dictionary<string, SupportedProviders>> s_providerStringToEnum =
                                                                                new(CreateProviderMappings);

        private readonly BlueskyAgent _agent = new( );
        private readonly SemaphoreSlim _authLock = new( 1, 1 );
        private bool _isAuthenticated;


        /// <inheritdoc/>
        public async Task<string> StoreMediaLinkResultAsync( MediaLinkResult result ) {
            await EnsureAuthenticatedAsync( );

            try {
                // Generate deterministic rkey based on externalId or metadata
                string rkey = RecordKeyGenerator.GenerateRkey( result );

                // Convert MediaLinkResult DTO to custom record
                MediaLinkResultRecord record = ConvertToRecord( result );

                // Try to update existing record first (upsert logic)
                RecordKey recordKey = new( rkey );
                AtProtoHttpResult<PutRecordResult> putResult = await _agent.PutRecord(
                    record: record,
                    collection: s_mediaLinkResultCollection,
                    rKey: recordKey,
                    validate: false // PDS Resolution not enabled yet
                );

                if (putResult.Succeeded && putResult.Result is not null) {
                    logger.LogInformation( "Successfully updated existing MediaLinkResult on ATProto PDS: {uri}", putResult.Result.Uri );
                    return putResult.Result.Uri.ToString( );
                }

                // If update failed, try to create new record
                AtProtoHttpResult<CreateRecordResult> createResult = await _agent.CreateRecord(
                    record: record,
                    collection: s_mediaLinkResultCollection,
                    rKey: recordKey,
                    validate: false // PDS Resolution not enabled yet
                );

                if (!createResult.Succeeded || createResult.Result is null) {
                    string errorMsg = createResult.AtErrorDetail?.Message ?? $"HTTP {createResult.StatusCode}";
                    throw new InvalidOperationException( $"Failed to create record on ATProto PDS: {errorMsg}" );
                }

                logger.LogInformation( "Successfully created MediaLinkResult on ATProto PDS: {uri}", createResult.Result.Uri );

                return createResult.Result.Uri.ToString( );
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to store MediaLinkResult on ATProto PDS" );
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri ) {
            await EnsureAuthenticatedAsync( );

            try {
                // Parse the AT-URI
                AtUri atUri = new( recordUri );

                // Get the record from ATProto PDS
                AtProtoHttpResult<AtProtoRepositoryRecord<MediaLinkResultRecord>> getRecordResult = await _agent.GetRecord<MediaLinkResultRecord>(
                    uri: atUri,
                    cid: null
                );

                if (getRecordResult.Succeeded && getRecordResult.Result?.Value is not null) {
                    // Convert the custom record back to MediaLinkResult DTO
                    return ConvertFromRecord( getRecordResult.Result.Value );
                } else if (!getRecordResult.Succeeded) {
                    string errorMsg = getRecordResult.AtErrorDetail?.Message ?? $"HTTP {getRecordResult.StatusCode}";
                    logger.LogWarning( "Failed to get record from ATProto PDS: {error}", errorMsg );
                }

                return null;
            } catch (Exception ex) {
                logger.LogError( ex, "Failed to retrieve MediaLinkResult from ATProto PDS: {uri}", recordUri );
                return null;
            }
        }

        /// <summary>
        /// Ensures the agent is authenticated with ATProto PDS.
        /// </summary>
        private async Task EnsureAuthenticatedAsync( ) {
            if (_isAuthenticated) { return; }

            await _authLock.WaitAsync( );
            try {
                if (_isAuthenticated) { return; }

                AtProtoHttpResult<bool> loginResult = await _agent.Login( identifier, password );
                if (!loginResult.Succeeded) {
                    string errorMsg = loginResult.AtErrorDetail?.Message ?? $"HTTP {loginResult.StatusCode}";
                    throw new InvalidOperationException( $"Failed to authenticate to PDS: {errorMsg}" );
                }

                _isAuthenticated = true;
                logger.LogInformation( "Successfully authenticated to PDS" );
            } finally {
                _ = _authLock.Release( );
            }
        }

        /// <summary>
        /// Converts a MediaLinkResult DTO to a MediaLinkResultRecord for storage.
        /// Note: Input links are NOT included in the PDS record for user privacy.
        /// They are tracked only in SQLite.
        /// </summary>
        private static MediaLinkResultRecord ConvertToRecord( MediaLinkResult result ) {
            List<ProviderResultRecord> providerResults = [];

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
            MediaLinkResult result = new( ){
                LookedUpAt = record.LookedUpAt.UtcDateTime
            };

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

            return !string.IsNullOrWhiteSpace( providerString )
                && s_providerStringToEnum.Value.TryGetValue( providerString, out provider );
        }
    }
}
