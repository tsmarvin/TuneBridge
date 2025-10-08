using System.Text.Json;
using System.Text.RegularExpressions;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Extensions;
using TuneBridge.Domain.Interfaces;
using TuneBridge.Domain.Types.Enums;

namespace TuneBridge.Domain.Types.Bases {

    /// <summary>
    /// Abstract base class providing shared infrastructure for media link aggregation services.
    /// Implements common patterns for querying multiple music provider APIs in parallel, deduplicating
    /// results based on external IDs (ISRC/UPC), and merging metadata from different sources into unified
    /// <see cref="MediaLinkResult"/> objects.
    /// </summary>
    /// <param name="enabledProvidersCollection">
    /// Dictionary mapping <see cref="SupportedProviders"/> to their respective API service implementations.
    /// Allows runtime configuration of which providers are active (e.g., only Spotify, only Apple Music, or both).
    /// </param>
    /// <param name="logger">Logger for tracking API failures, cross-platform matching issues, and performance metrics.</param>
    /// <param name="serializerOptions">
    /// JSON serialization settings used when logging complex API responses for debugging. Typically configured
    /// with indentation enabled to improve readability in log files.
    /// </param>
    /// <remarks>
    /// Derived classes must implement the four core lookup methods. The base class provides helper methods for
    /// parallel provider queries, URL extraction, and result deduplication.
    /// </remarks>
    public abstract partial class MediaLinkServiceBase(
        Dictionary<SupportedProviders, IMusicLookupService> enabledProvidersCollection,
        ILogger<MediaLinkServiceBase> logger,
        JsonSerializerOptions serializerOptions
    ) : IMediaLinkService {

        public abstract IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content );
        public abstract Task<MediaLinkResult?> GetInfoAsync( string title, string artist );
        public abstract Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc );
        public abstract Task<MediaLinkResult?> GetInfoByUPCAsync( string upc );

        #region Base Class Defaults

        protected readonly ILogger<MediaLinkServiceBase> Logger = logger;
        protected readonly JsonSerializerOptions SerializerOptions = serializerOptions;
        protected readonly Dictionary<SupportedProviders, IMusicLookupService> EnabledProviders = enabledProvidersCollection;

        protected virtual Regex ValidLink { get; init; } = ValidHttpsLink( );

        /// <summary>
        /// Performs the initial link extraction and lookup for all services.
        /// </summary>
        /// <param name="content">The string content to parse for links.</param>
        /// <returns>A dictionary with the <see cref="MusicLookupResultDto"/> as the key, and a tuple containing the provider and inputlink information as the value.</returns>
        protected async Task<Dictionary<MusicLookupResultDto, (SupportedProviders provider, string inputLink)>> GetMusicLookupResults( string content ) {
            Dictionary<MusicLookupResultDto, (SupportedProviders provider, string inputLink)> linkResults = [];
            try {
                foreach (string link in ValidLink.GetGroupValues( content, "Link" )) {
                    if (string.IsNullOrWhiteSpace( link )) { continue; }

                    foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                        try {
                            MusicLookupResultDto? lookup = await svc.GetInfoAsync( link );
                            if (lookup is not null) { linkResults.Add( lookup, (provider, link) ); }
                        } catch (Exception e) {
                            Logger.LogError( e, "Failed while getting initial media link lookup data by URL for {provider}.", provider );
                            Logger.LogTrace( $"link: {link}" );
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by URL." );
                Logger.LogTrace( $"Content: {content}" );
            }
            return linkResults;
        }

        /// <summary>
        /// Performs the initial title/artist lookup for all services.
        /// </summary>
        /// <param name="title">The name of the track or album.</param>
        /// <param name="artist">The artist that created the track or album.</param>
        /// <returns>A tuple containing the <see cref="MusicLookupResultDto"/> and <see cref="SupportedProviders">.</returns>
        protected async Task<(MusicLookupResultDto result, SupportedProviders provider)?> GetMusicLookupResults( string title, string artist ) {
            try {
                if (string.IsNullOrWhiteSpace( title ) || string.IsNullOrWhiteSpace( artist )) { return null; }

                foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                    MusicLookupResultDto? lookup = await svc.GetInfoAsync( title, artist );
                    if (lookup is not null) { return (lookup, provider); }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by artist/title." );
                Logger.LogTrace( $"title: '{title}', artist: '{artist}'" );
            }
            return null;
        }

        /// <summary>
        /// Performs the initial external_id lookup for all services.
        /// </summary>
        /// <param name="externalId">The string content to parse for links.</param>
        /// <param name="isAlbum">Indicates whether to search for UPC entries (true) or ISRC entries (false).</param>
        /// <returns>A tuple containing the <see cref="MusicLookupResultDto"/> and <see cref="SupportedProviders">.</returns>
        protected async Task<(MusicLookupResultDto result, SupportedProviders provider)?> GetMusicLookupResults( string externalId, bool isAlbum ) {
            try {
                if (string.IsNullOrWhiteSpace( externalId )) { return null; }

                foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                    MusicLookupResultDto? lookup = isAlbum
                                                    ? await svc.GetInfoByUPCAsync( externalId )
                                                    : await svc.GetInfoByISRCAsync( externalId );

                    if (lookup is not null) { return (lookup, provider); }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by artist/title." );
                Logger.LogTrace( $"externalId: '{externalId}', isAlbum: {isAlbum}" );
            }

            return null;
        }

        protected async Task<MediaLinkResult?> CombineLookupInfoAsync(
            (MusicLookupResultDto dto, SupportedProviders provider)? lookupResults
        ) {
            if (lookupResults is null) { return null; }
            MediaLinkResult result = new();
            result.Results.Add( lookupResults.Value.provider, lookupResults.Value.dto );
            return await SyncLookupResult( result );
        }

        protected async IAsyncEnumerable<MediaLinkResult> CombineLookupInfoAsync(
            Dictionary<MusicLookupResultDto, (SupportedProviders provider, string inputLink)> linkResults
        ) {
            List<MediaLinkResult> results = [];
            Dictionary<SupportedProviders, IEnumerable<MusicLookupResultDto>> resultsByProvider = [];
            foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                IEnumerable<MusicLookupResultDto> providerResults = linkResults
                                                                        .Where( kv => kv.Value.provider == provider )
                                                                        .Select( lr => lr.Key );

                if (providerResults.Any( )) {
                    resultsByProvider.Add( provider, providerResults );
                }
            }

            foreach ((MusicLookupResultDto lookup, (SupportedProviders provider, string inputlink)) in linkResults) {
                // Deduplicate MusicLookupResultDto's from output results.
                if (results.Any( r => r.Results.Any( rr => rr.Key == provider && rr.Value == lookup ) )) {
                    continue;
                }

                MediaLinkResult result = new();
                lookup.IsPrimary = true;
                result._inputLinks.Add( $"https://{inputlink}" );
                result.Results.Add( provider, lookup );

                foreach ((SupportedProviders alternateProvider, IEnumerable<MusicLookupResultDto> altProviderResults) in resultsByProvider) {
                    if ((int)alternateProvider == (int)provider) { continue; }
                    MusicLookupResultDto? altProviderMatch = altProviderResults
                                                                .FirstOrDefault( a => a.ExternalId == lookup.ExternalId )
                                                           ?? altProviderResults
                                                                .FirstOrDefault( r =>
                                                                    r.Artist.Trim( ).Equals( lookup.Artist.Trim( ), StringComparison.InvariantCultureIgnoreCase ) &&
                                                                    r.Title.Trim( ).Equals( lookup.Title.Trim( ), StringComparison.InvariantCultureIgnoreCase )
                                                            );

                    if (altProviderMatch is not null) {
                        altProviderMatch.IsPrimary = false;
                        result.Results.Add( alternateProvider, altProviderMatch );
                    }
                }

                MediaLinkResult outputResult = await SyncLookupResult( result );
                results.Add( outputResult );
                yield return outputResult;
            }
        }

        #endregion Base Class Defaults

        #region Base Class Private Implementations

        private protected virtual async Task<MediaLinkResult> SyncLookupResult( MediaLinkResult input ) {
            if (input.Results.Count == 0) { return input; }

            List<SupportedProviders> completedList = [.. input.Results.Select( p => p.Key )];
            MusicLookupResultDto firstValue = input.Results.Values.First( );

            foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders.Where( e => completedList.Contains( e.Key ) == false )) {
                try {
                    MusicLookupResultDto? lookup = await svc.GetInfoAsync( firstValue );
                    if (lookup is not null) { input.Results.Add( provider, lookup ); }
                } catch (Exception ex) {
                    Logger.LogError( ex,
                        "Error during secondary lookup via {additionalProvider} for artist '{artist}', title " +
                        "'{title}' externalId '{externalId}' isAlbum={isAlbum} originalProvider(s)={provider}",
                        provider, firstValue.Artist, firstValue.Title, firstValue.ExternalId, firstValue.IsAlbum,
                        string.Join( ", ", completedList.Select( l => l.ToString( ).ToArray( ) ) )
                    );
                    Logger.LogTrace( JsonSerializer.Serialize( input, SerializerOptions ) );
                }
            }
            return input;
        }

        [GeneratedRegex( @"[Hh][Tt]{2}[Pp][Ss]:\/\/(?<Link>\w[\w\/\=\?\.\:\-%&]*)" )]
        private protected static partial Regex ValidHttpsLink( );

        #endregion Base Class Private Implementations

    }
}
