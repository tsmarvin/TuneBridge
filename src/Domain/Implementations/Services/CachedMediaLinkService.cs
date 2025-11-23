using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Bases;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// Decorator for <see cref="IMediaLinkService"/> that adds caching with ATProto PDS storage.
    /// This service checks the cache before performing lookups and stores results for future use.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="CachingMediaLinkService"/> class.
    /// </remarks>
    /// <param name="enabledProvidersCollection">Dictionary of active provider services keyed by provider type.</param>
    /// <param name="cacheRepository">The cache repository for storing and retrieving results.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="serializerOptions">JSON serialization options for logging.</param>
    public partial class CachingMediaLinkService(
        Dictionary<SupportedProviders, IMusicLookupService> enabledProvidersCollection,
        IMediaLinkCacheRepository cacheRepository,
        ILogger<CachingMediaLinkService> logger,
        JsonSerializerOptions serializerOptions
    ) : MediaLinkServiceBase( enabledProvidersCollection, logger, serializerOptions ) {

        private readonly IMediaLinkCacheRepository _cacheRepository = cacheRepository;

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoAsync( string title, string artist ) {
            // Check cache first
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                await _cacheRepository.TryGetCachedResultByMetadataAsync( title, artist );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for title/artist lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            (MusicLookupResultDto result, SupportedProviders provider)? lookupResult =
                                                                            await GetMusicLookupResults( title, artist );
            if (lookupResult is null) { return null; }

            MediaLinkResult result = (await CombineLookupInfoAsync( lookupResult ))!;
            _ = await _cacheRepository.CacheResultAsync( result );

            return result;
        }

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc ) {
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                                await _cacheRepository.TryGetCachedResultByISRCAsync( isrc );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for ISRC lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            (MusicLookupResultDto result, SupportedProviders provider)? lookupResult =
                                                                            await GetMusicLookupResults( isrc, false );
            if (lookupResult is null) { return null; }

            MediaLinkResult result = (await CombineLookupInfoAsync( lookupResult ))!;
            _ = await _cacheRepository.CacheResultAsync( result );

            return result;
        }

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoByUPCAsync( string upc ) {
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                await _cacheRepository.TryGetCachedResultByUPCAsync( upc );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for UPC lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            (MusicLookupResultDto result, SupportedProviders provider)? lookupResult =
                                                                            await GetMusicLookupResults( upc, true );
            if (lookupResult is null) { return null; }

            MediaLinkResult result = (await CombineLookupInfoAsync( lookupResult ))!;
            _ = await _cacheRepository.CacheResultAsync( result );

            return result;
        }

        /// <inheritdoc/>
        public override async Task<MediaLinkResult?> GetInfoByProviderIdAsync(
            string providerId,
            SupportedProviders provider,
            bool isAlbum
        ) {
            // Check cache first by provider ID
            (MediaLinkResult result, string recordUri, bool isStale)? cachedResult =
                await _cacheRepository.TryGetCachedResultByProviderIdAsync( providerId, provider, isAlbum );

            if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for provider ID lookup", cachedResult.Value.recordUri );
                return cachedResult.Value.result;
            }

            // Perform fresh lookup using base class helper methods
            MusicLookupResultDto? lookupResult = await GetMusicLookupResultsByProviderId( providerId, provider, isAlbum );
            if (lookupResult is null) { return null; }

            MediaLinkResult result = (await CombineLookupInfoAsync( (lookupResult, provider) ))!;
            _ = await _cacheRepository.CacheResultAsync( result );

            return result;
        }

        /// <inheritdoc/>
        public override async IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content ) {
            // Track which links we've already processed to avoid duplicates
            HashSet<string> processedLinks = new( StringComparer.OrdinalIgnoreCase );
            // Check cache for each extracted link
            foreach (string link in ValidLink.GetGroupValues( content, "Url" )) {
                if (!processedLinks.Add( link )) { continue; }

                (MediaLinkResult result, string recordUri, bool isStale)? cachedResult = await _cacheRepository.TryGetCachedResultAsync( link );
                if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                    Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for provider ID lookup", cachedResult.Value.recordUri );
                    yield return cachedResult.Value.result;
                    continue;
                }

                Dictionary<MusicLookupResultDto, (SupportedProviders provider, string inputLink)> lookupResult =
                                                                            await GetMusicLookupResults( link );
                if (lookupResult.Count == 0) { continue; }

                foreach ((MusicLookupResultDto dto, (SupportedProviders provider, _)) in lookupResult) {
                    cachedResult = dto.IsAlbum ?? false
                            ? await _cacheRepository.TryGetCachedResultByUPCAsync( dto.ExternalId )
                            : await _cacheRepository.TryGetCachedResultByISRCAsync( dto.ExternalId );

                    if (cachedResult.HasValue && !cachedResult.Value.isStale) {
                        Logger.LogInformation( "Using fresh cached result from RecordUri: {RecordUri} for provider ID lookup", cachedResult.Value.recordUri );
                        yield return cachedResult.Value.result;
                        continue;
                    }

                    MediaLinkResult result = (await CombineLookupInfoAsync( (dto, provider) ))!;
                    _ = await _cacheRepository.CacheResultAsync( result );
                    yield return result;
                }
            }

        }
    }
}
