using System.Text.Json;
using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Infrastructure.Utilities;

namespace BridgeBeats.Services.LinkResolver {

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

        /// <inheritdoc/>
        public abstract IAsyncEnumerable<MediaLinkResult> GetInfoAsync( string content );
        /// <inheritdoc/>
        public abstract Task<MediaLinkResult?> GetInfoAsync( string title, string artist );
        /// <inheritdoc/>
        public abstract Task<MediaLinkResult?> GetInfoByISRCAsync( string isrc );
        /// <inheritdoc/>
        public abstract Task<MediaLinkResult?> GetInfoByUPCAsync( string upc );
        /// <inheritdoc/>
        public abstract Task<MediaLinkResult?> GetInfoByProviderIdAsync( string providerId, SupportedProviders provider, bool isAlbum );

        #region Base Class Defaults

        /// <summary>Logger for tracking API failures and cross-platform matching issues.</summary>
        protected readonly ILogger<MediaLinkServiceBase> Logger = logger;
        /// <summary>JSON serialization settings for logging API responses.</summary>
        protected readonly JsonSerializerOptions SerializerOptions = serializerOptions;
        /// <summary>Dictionary of active music provider service implementations.</summary>
        protected readonly Dictionary<SupportedProviders, IMusicLookupService> EnabledProviders = enabledProvidersCollection;

        /// <summary>Regex pattern for validating HTTPS links.</summary>
        protected virtual Regex ValidLink { get; init; } = ValidHttpsLink( );

        /// <summary>
        /// Performs the initial link extraction and lookup for all services.
        /// </summary>
        /// <param name="content">The string content to parse for links.</param>
        /// <returns>A dictionary with the <see cref="MusicLookupResult"/> as the key, and a tuple containing the provider and inputlink information as the value.</returns>
        protected async Task<Dictionary<MusicLookupResult, (SupportedProviders provider, string inputLink)>> GetMusicLookupResults( string content ) {
            Dictionary<MusicLookupResult, (SupportedProviders provider, string inputLink)> linkResults = [];
            try {
                foreach (string link in ValidLink.GetGroupValues( content, "Link" )) {
                    if (string.IsNullOrWhiteSpace( link )) { continue; }

                    foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                        try {
                            MusicLookupResult? lookup = await svc.GetInfoAsync( link );
                            if (lookup is not null) { linkResults.Add( lookup, (provider, link) ); }
                        } catch (Exception e) {
                            Logger.LogError( e, "Failed while getting initial media link lookup data by URL for {provider}.", provider );
                            Logger.LogTrace( "link: {link}", link.SanitizeForLogging( ) );
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by URL." );
                Logger.LogTrace( "Content: {content}", content.SanitizeForLogging( ) );
            }
            return linkResults;
        }

        /// <summary>
        /// Performs the initial title/artist lookup for all services.
        /// </summary>
        /// <param name="title">The name of the track or album.</param>
        /// <param name="artist">The artist that created the track or album.</param>
        /// <returns>A tuple containing the <see cref="MusicLookupResult"/> and <see cref="SupportedProviders"/>.</returns>
        protected async Task<(MusicLookupResult result, SupportedProviders provider)?> GetMusicLookupResults( string title, string artist ) {
            try {
                if (string.IsNullOrWhiteSpace( title ) || string.IsNullOrWhiteSpace( artist )) { return null; }

                foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                    try {
                        MusicLookupResult? lookup = await svc.GetInfoAsync( title, artist );
                        if (lookup is not null) { return (lookup, provider); }
                    } catch (Exception ex) {
                        Logger.LogError( ex, "Failed while getting initial media link lookup data by artist/title for {provider}.", provider );
                        Logger.LogTrace( "title: '{title}', artist: '{artist}'", title.SanitizeForLogging( ), artist.SanitizeForLogging( ) );
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by artist/title." );
                Logger.LogTrace( "title: '{title}', artist: '{artist}'", title.SanitizeForLogging( ), artist.SanitizeForLogging( ) );
            }
            return null;
        }

        /// <summary>
        /// Performs the initial external_id lookup for all services.
        /// </summary>
        /// <param name="externalId">The string content to parse for links.</param>
        /// <param name="isAlbum">Indicates whether to search for UPC entries (true) or ISRC entries (false).</param>
        /// <returns>A tuple containing the <see cref="MusicLookupResult"/> and <see cref="SupportedProviders"/>.</returns>
        protected async Task<(MusicLookupResult result, SupportedProviders provider)?> GetMusicLookupResults( string externalId, bool isAlbum ) {
            try {
                if (string.IsNullOrWhiteSpace( externalId )) { return null; }

                foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                    try {
                        MusicLookupResult? lookup = isAlbum
                                                        ? await svc.GetInfoByUPCAsync( externalId )
                                                        : await svc.GetInfoByISRCAsync( externalId );

                        if (lookup is not null) { return (lookup, provider); }
                    } catch (Exception ex) {
                        Logger.LogError( ex, "Failed while getting initial media link lookup data by externalId for {provider}.", provider );
                        Logger.LogTrace( "externalId: '{externalId}', isAlbum: {isAlbum}", externalId.SanitizeForLogging( ), isAlbum );
                    }
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by artist/title." );
                Logger.LogTrace( "externalId: '{externalId}', isAlbum: {isAlbum}", externalId.SanitizeForLogging( ), isAlbum );
            }

            return null;
        }

        /// <summary>
        /// Performs the initial provider ID lookup for a specific service.
        /// </summary>
        /// <param name="providerId">The provider-specific identifier.</param>
        /// <param name="provider">The provider to query.</param>
        /// <param name="isAlbum">Indicates whether to search for album entries (true) or track entries (false).</param>
        /// <returns>The <see cref="MusicLookupResult"/> for the <paramref name="provider"/>.</returns>
        protected async Task<MusicLookupResult?> GetMusicLookupResultsByProviderId(
            string providerId,
            SupportedProviders provider,
            bool isAlbum
        ) {
            try {
                if (string.IsNullOrWhiteSpace( providerId )) { return null; }

                if (!EnabledProviders.TryGetValue( provider, out IMusicLookupService? svc )) {
                    Logger.LogWarning( "Provider {provider} is not enabled or configured", provider );
                    return null;
                }
                MusicLookupResult? lookup = await svc.GetInfoByIDAsync( providerId, isAlbum );
                if (lookup is not null) {
                    lookup.IsPrimary = true;
                    return lookup;
                }
            } catch (Exception ex) {
                Logger.LogError( ex, "Failed while getting initial media link lookup data by providerId." );
                Logger.LogTrace( "providerId: '{providerId}', provider: {provider}, isAlbum: {isAlbum}",
                    providerId.SanitizeForLogging( ), provider, isAlbum );
            }

            return null;
        }

        /// <summary>
        /// Combines lookup results from a single provider into a MediaLinkResult and syncs with other providers.
        /// </summary>
        /// <param name="lookupResults">Optional tuple containing the DTO and provider information.</param>
        /// <returns>A MediaLinkResult with cross-platform data, or null if input is null.</returns>
        protected async Task<MediaLinkResult?> CombineLookupInfoAsync(
            (MusicLookupResult dto, SupportedProviders provider)? lookupResults
        ) {
            if (lookupResults is null) { return null; }
            MediaLinkResult result = new();
            result.Results.Add( lookupResults.Value.provider, lookupResults.Value.dto );
            return await SyncLookupResult( result );
        }

        /// <summary>
        /// Combines lookup results from multiple providers and input links into deduplicated MediaLinkResults.
        /// </summary>
        /// <param name="linkResults">Dictionary mapping DTOs to their provider and input link information.</param>
        /// <returns>Async enumerable of MediaLinkResults with cross-platform data.</returns>
        protected async IAsyncEnumerable<MediaLinkResult> CombineLookupInfoAsync(
            Dictionary<MusicLookupResult, (SupportedProviders provider, string inputLink)> linkResults
        ) {
            List<MediaLinkResult> results = [];
            Dictionary<SupportedProviders, IEnumerable<MusicLookupResult>> resultsByProvider = [];
            foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders) {
                IEnumerable<MusicLookupResult> providerResults = linkResults
                                                                        .Where( kv => kv.Value.provider == provider )
                                                                        .Select( lr => lr.Key );

                if (providerResults.Any( )) {
                    resultsByProvider.Add( provider, providerResults );
                }
            }

            foreach ((MusicLookupResult lookup, (SupportedProviders provider, string inputlink)) in linkResults) {
                // Deduplicate MusicLookupResultDto's from output results.
                if (results.Any( r => r.Results.Any( rr => rr.Key == provider && rr.Value == lookup ) )) {
                    continue;
                }

                MediaLinkResult result = new();
                lookup.IsPrimary = true;
                result._inputLinks.Add( $"https://{inputlink}" );
                if (lookup.ExternalId == "prerelease") {
                    result.Messages ??= [];
                    result.Messages.Add( "Prerelease links aren't supported at the moment. Try a Title/Artist search to check other platforms." );
                } else {
                    result.Results.Add( provider, lookup );
                }

                foreach ((SupportedProviders alternateProvider, IEnumerable<MusicLookupResult> altProviderResults) in resultsByProvider) {
                    if ((int)alternateProvider == (int)provider) { continue; }
                    MusicLookupResult? altProviderMatch = altProviderResults
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
            MusicLookupResult firstValue = input.Results.Values.First( );

            foreach ((SupportedProviders provider, IMusicLookupService svc) in EnabledProviders.Where( e => completedList.Contains( e.Key ) == false )) {
                try {
                    MusicLookupResult? lookup = await svc.GetInfoAsync( firstValue );
                    if (lookup is not null) { input.Results.Add( provider, lookup ); }
                } catch (Exception ex) {
                    Logger.LogError( ex,
                        "Error during secondary lookup via {additionalProvider} for artist '{artist}', title " +
                        "'{title}' externalId '{externalId}' isAlbum={isAlbum} originalProvider(s)={provider}",
                        provider,
                        firstValue.Artist,
                        firstValue.Title,
                        firstValue.ExternalId,
                        firstValue.IsAlbum,
                        string.Join( ", ", completedList.Select( l => l.ToString( ) ) )
                    );
                    Logger.LogTrace( "Input data: {InputData}", JsonSerializer.Serialize( input, SerializerOptions ) );
                }
            }
            return input;
        }

        [GeneratedRegex( @"(?<Url>[Hh][Tt]{2}[Pp][Ss]:\/\/(?<Link>\w[\w\/\=\?\.\:\-%&]*))" )]
        private protected static partial Regex ValidHttpsLink( );

        #endregion Base Class Private Implementations

    }
}
