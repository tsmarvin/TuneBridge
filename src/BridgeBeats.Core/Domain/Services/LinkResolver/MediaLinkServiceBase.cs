using System.Text.Json;
using System.Text.RegularExpressions;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Domain.Providers.Common;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Utilities;

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
                            LogUrlLookupProviderError( Logger, e, provider );
                            if (Logger.IsEnabled( LogLevel.Trace )) {
                                string sanitizedLink = link.SanitizeForLogging( );
                                LogUrlLookupProviderTrace( Logger, sanitizedLink );
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                LogUrlLookupError( Logger, ex );
                if (Logger.IsEnabled( LogLevel.Trace )) {
                    string sanitizedContent = content.SanitizeForLogging( );
                    LogUrlLookupTrace( Logger, sanitizedContent );
                }
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
                        LogArtistTitleLookupProviderError( Logger, ex, provider );
                        if (Logger.IsEnabled( LogLevel.Trace )) {
                            string sanitizedTitle = title.SanitizeForLogging( );
                            string sanitizedArtist = artist.SanitizeForLogging( );
                            LogArtistTitleLookupProviderTrace( Logger, sanitizedTitle, sanitizedArtist );
                        }
                    }
                }
            } catch (Exception ex) {
                LogArtistTitleLookupError( Logger, ex );
                if (Logger.IsEnabled( LogLevel.Trace )) {
                    string sanitizedTitle = title.SanitizeForLogging( );
                    string sanitizedArtist = artist.SanitizeForLogging( );
                    LogArtistTitleLookupTrace( Logger, sanitizedTitle, sanitizedArtist );
                }
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
                        LogExternalIdLookupProviderError( Logger, ex, provider );
                        if (Logger.IsEnabled( LogLevel.Trace )) {
                            string sanitizedExternalId = externalId.SanitizeForLogging( );
                            LogExternalIdLookupProviderTrace( Logger, sanitizedExternalId, isAlbum );
                        }
                    }
                }
            } catch (Exception ex) {
                LogExternalIdLookupError( Logger, ex );
                if (Logger.IsEnabled( LogLevel.Trace )) {
                    string sanitizedExternalId = externalId.SanitizeForLogging( );
                    LogExternalIdLookupTrace( Logger, sanitizedExternalId, isAlbum );
                }
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
                    LogProviderNotEnabled( Logger, provider );
                    return null;
                }
                MusicLookupResult? lookup = await svc.GetInfoByIDAsync( providerId, isAlbum );
                if (lookup is not null) {
                    lookup.IsPrimary = true;
                    return lookup;
                }
            } catch (Exception ex) {
                LogProviderIdLookupError( Logger, ex );
                if (Logger.IsEnabled( LogLevel.Trace )) {
                    string sanitizedProviderId = providerId.SanitizeForLogging( );
                    LogProviderIdLookupTrace( Logger, sanitizedProviderId, provider, isAlbum );
                }
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
                result.InputLinks.Add( $"https://{inputlink}" );
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
                    if (Logger.IsEnabled( LogLevel.Error )) {
                        LogSecondaryLookupError( Logger, ex, provider, firstValue.Artist, firstValue.Title, firstValue.ExternalId, firstValue.IsAlbum ?? false, string.Join( ", ", completedList.Select( l => l.ToString( ) ) ) );
                    }
                    if (Logger.IsEnabled( LogLevel.Trace )) {
                        string serializedInput = JsonSerializer.Serialize( input, SerializerOptions );
                        LogSecondaryLookupTrace( Logger, serializedInput );
                    }
                }
            }
            return input;
        }

        [GeneratedRegex( @"(?<Url>[Hh][Tt]{2}[Pp][Ss]:\/\/(?<Link>\w[\w\/\=\?\.\:\-%&]*))" )]
        private protected static partial Regex ValidHttpsLink( );

        #endregion Base Class Private Implementations

        #region LoggerMessage Definitions

        /// <summary>
        /// Logs an error when URL lookup fails for a specific provider.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.UrlLookupProviderError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by URL for {Provider}." )]
        private static partial void LogUrlLookupProviderError( ILogger logger, Exception ex, SupportedProviders provider );

        /// <summary>
        /// Logs trace-level information for URL lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.UrlLookupProviderTrace,
            Level = LogLevel.Trace,
            Message = "link: {Link}" )]
        private static partial void LogUrlLookupProviderTrace( ILogger logger, string link );

        /// <summary>
        /// Logs an error when URL lookup fails.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.UrlLookupError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by URL." )]
        private static partial void LogUrlLookupError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs trace-level content for URL lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.UrlLookupTrace,
            Level = LogLevel.Trace,
            Message = "Content: {Content}" )]
        private static partial void LogUrlLookupTrace( ILogger logger, string content );

        /// <summary>
        /// Logs an error when artist/title lookup fails for a specific provider.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ArtistTitleLookupProviderError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by artist/title for {Provider}." )]
        private static partial void LogArtistTitleLookupProviderError( ILogger logger, Exception ex, SupportedProviders provider );

        /// <summary>
        /// Logs trace-level information for artist/title lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ArtistTitleLookupProviderTrace,
            Level = LogLevel.Trace,
            Message = "title: '{Title}', artist: '{Artist}'" )]
        private static partial void LogArtistTitleLookupProviderTrace( ILogger logger, string title, string artist );

        /// <summary>
        /// Logs an error when artist/title lookup fails.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ArtistTitleLookupError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by artist/title." )]
        private static partial void LogArtistTitleLookupError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs trace-level information for artist/title lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ArtistTitleLookupTrace,
            Level = LogLevel.Trace,
            Message = "title: '{Title}', artist: '{Artist}'" )]
        private static partial void LogArtistTitleLookupTrace( ILogger logger, string title, string artist );

        /// <summary>
        /// Logs an error when external ID lookup fails for a specific provider.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ExternalIdLookupProviderError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by externalId for {Provider}." )]
        private static partial void LogExternalIdLookupProviderError( ILogger logger, Exception ex, SupportedProviders provider );

        /// <summary>
        /// Logs trace-level information for external ID lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ExternalIdLookupProviderTrace,
            Level = LogLevel.Trace,
            Message = "externalId: '{ExternalId}', isAlbum: {IsAlbum}" )]
        private static partial void LogExternalIdLookupProviderTrace( ILogger logger, string externalId, bool isAlbum );

        /// <summary>
        /// Logs an error when external ID lookup fails.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ExternalIdLookupError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by externalId." )]
        private static partial void LogExternalIdLookupError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs trace-level information for external ID lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ExternalIdLookupTrace,
            Level = LogLevel.Trace,
            Message = "externalId: '{ExternalId}', isAlbum: {IsAlbum}" )]
        private static partial void LogExternalIdLookupTrace( ILogger logger, string externalId, bool isAlbum );

        /// <summary>
        /// Logs a warning when a provider is not enabled.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ProviderNotEnabled,
            Level = LogLevel.Warning,
            Message = "Provider {Provider} is not enabled or configured" )]
        private static partial void LogProviderNotEnabled( ILogger logger, SupportedProviders provider );

        /// <summary>
        /// Logs an error when provider ID lookup fails.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ProviderIdLookupError,
            Level = LogLevel.Error,
            Message = "Failed while getting initial media link lookup data by providerId." )]
        private static partial void LogProviderIdLookupError( ILogger logger, Exception ex );

        /// <summary>
        /// Logs trace-level information for provider ID lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.ProviderIdLookupTrace,
            Level = LogLevel.Trace,
            Message = "providerId: '{ProviderId}', provider: {Provider}, isAlbum: {IsAlbum}" )]
        private static partial void LogProviderIdLookupTrace( ILogger logger, string providerId, SupportedProviders provider, bool isAlbum );

        /// <summary>
        /// Logs an error when secondary lookup fails.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.SecondaryLookupError,
            Level = LogLevel.Error,
            Message = "Error during secondary lookup via {AdditionalProvider} for artist '{Artist}', title '{Title}' externalId '{ExternalId}' isAlbum={IsAlbum} originalProvider(s)={Provider}" )]
        private static partial void LogSecondaryLookupError( ILogger logger, Exception ex, SupportedProviders additionalProvider, string artist, string title, string externalId, bool isAlbum, string provider );

        /// <summary>
        /// Logs trace-level input data for secondary lookup.
        /// </summary>
        [LoggerMessage(
            EventId = LogEventIds.Services.LinkResolver.SecondaryLookupTrace,
            Level = LogLevel.Trace,
            Message = "Input data: {InputData}" )]
        private static partial void LogSecondaryLookupTrace( ILogger logger, string inputData );

        #endregion LoggerMessage Definitions

    }
}
