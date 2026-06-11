using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Logging;
using BridgeBeats.Core.Infrastructure.Storage.Car;
using BridgeBeats.Core.Infrastructure.Utilities;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;

namespace BridgeBeats.Core.Infrastructure.Storage;

/// <summary>
/// Implementation of <see cref="IATProtoStorageService"/> that stores MediaLinkResult records on ATProto PDS
/// as custom lexicon records using the AT Protocol.
/// </summary>
/// <remarks>
/// This service uses the idunno.Bluesky library to interact with ATProto-compatible PDS instances.
/// MediaLinkResults are stored as custom link.bridgebeats.lookup lexicon records.
/// Authentication is managed centrally by <see cref="IATProtoSessionManager"/> to reduce PDS API calls.
/// Record enumeration uses a single CAR v1 download (com.atproto.sync.getRepo) instead of
/// paginated listRecords calls, reducing N/100 round-trips to a single HTTP request.
/// </remarks>
/// <param name="sessionManager">The centralized session manager for authentication.</param>
/// <param name="logger">Logger for diagnostic information.</param>
/// <param name="httpClientFactory">Factory for creating named HTTP clients.</param>
public partial class ATProtoStorageService(
    IATProtoSessionManager sessionManager,
    ILogger<ATProtoStorageService> logger,
    IHttpClientFactory httpClientFactory
) : IATProtoStorageService {

    /// <summary>
    /// Named HTTP client used for com.atproto.sync.getRepo (large CAR file downloads).
    /// </summary>
    internal const string ATProtoSyncHttpClientName = "atproto-sync";

    /// <summary>
    /// The NSID (Namespaced Identifier) for the BridgeBeats MediaLinkResult lexicon.
    /// </summary>
    private static readonly Nsid s_mediaLinkResultCollection = new( "link.bridgebeats.lookup" );

    /// <summary>
    /// Static cached dictionary mapping provider strings to SupportedProviders enum values.
    /// Initialized once at startup for O(1) lookups.
    /// </summary>
    private static readonly Lazy<Dictionary<string, SupportedProviders>> s_providerStringToEnum = new( CreateProviderMappings );


    /// <inheritdoc/>
    public async Task<string> StoreMediaLinkResultAsync( MediaLinkResult result, CancellationToken cancellationToken = default ) {
        BlueskyAgent agent = await sessionManager.GetAuthenticatedAgentAsync( cancellationToken );

        try {
            // Generate deterministic rkey based on externalId or metadata
            string rkey = RecordKeyGenerator.GenerateRkey( result );

            // Convert MediaLinkResult DTO to custom record
            MediaLinkResultRecord record = ConvertToRecord( result );

            // Try to update existing record first (upsert logic)
            RecordKey recordKey = new( rkey );
            AtProtoHttpResult<PutRecordResult> putResult = await agent.PutRecord(
                record: record,
                collection: s_mediaLinkResultCollection,
                rKey: recordKey,
                validate: false, // PDS Resolution not enabled yet
                cancellationToken: cancellationToken
            );

            if (putResult.Succeeded && putResult.Result is not null) {
                string uri = putResult.Result.Uri.ToString( );
                if (logger.IsEnabled( LogLevel.Information )) {
                    LogUpdatedRecord( logger, uri );
                }
                return uri;
            }

            // If update failed, try to create new record
            AtProtoHttpResult<CreateRecordResult> createResult = await agent.CreateRecord(
                record: record,
                collection: s_mediaLinkResultCollection,
                rKey: recordKey,
                validate: false, // PDS Resolution not enabled yet
                cancellationToken: cancellationToken
            );

            if (!createResult.Succeeded || createResult.Result is null) {
                string errorMsg = createResult.AtErrorDetail?.Message ?? $"HTTP {createResult.StatusCode}";
                throw new InvalidOperationException( $"Failed to create record on ATProto PDS: {errorMsg}" );
            }

            string recordUri = createResult.Result.Uri.ToString( );
            if (logger.IsEnabled( LogLevel.Information )) {
                LogCreatedRecord( logger, recordUri );
            }

            return recordUri;
        } catch (Exception ex) {
            LogStoreError( logger, ex );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<MediaLinkResult?> GetMediaLinkResultAsync( string recordUri ) {
        BlueskyAgent agent = await sessionManager.GetAuthenticatedAgentAsync( );

        try {
            // Parse the AT-URI
            AtUri atUri = new( recordUri );

            // Get the record from ATProto PDS
            AtProtoHttpResult<AtProtoRepositoryRecord<MediaLinkResultRecord>> getRecordResult = await agent.GetRecord<MediaLinkResultRecord>(
                uri: atUri,
                cid: null
            );

            if (getRecordResult.Succeeded && getRecordResult.Result?.Value is not null) {
                // Convert the custom record back to MediaLinkResult DTO
                return ConvertFromRecord( getRecordResult.Result.Value );
            } else if (!getRecordResult.Succeeded) {
                string errorMsg = (getRecordResult.AtErrorDetail?.Message ?? $"HTTP {getRecordResult.StatusCode}").SanitizeForLogging( );
                LogGetRecordFailed( logger, errorMsg );
            }

            return null;
        } catch (Exception ex) {
            LogRetrieveError( logger, ex, recordUri );
            return null;
        }
    }

    /// <summary>
    /// Converts a MediaLinkResult DTO to a MediaLinkResultRecord for storage.
    /// Note: Input links are NOT included in the PDS record for user privacy.
    /// They are tracked only in SQLite.
    /// </summary>
    private static MediaLinkResultRecord ConvertToRecord( MediaLinkResult result ) {
        List<ProviderResultRecord> providerResults = [];

        foreach ((SupportedProviders provider, MusicLookupResult lookupResult) in result.Results) {
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
            isPartial: result.IsPartial
        );
    }

    /// <summary>
    /// Converts a MediaLinkResultRecord from storage back to a MediaLinkResult DTO.
    /// Note: Input links are not stored in PDS records, only provider results.
    /// </summary>
    /// <returns>A MediaLinkResult with parsed providers, or null if no valid providers were found.</returns>
    private static MediaLinkResult? ConvertFromRecord( MediaLinkResultRecord record ) {
        MediaLinkResult result = new( ) {
            LookedUpAt = record.LookedUpAt.UtcDateTime,
            IsPartial = record.IsPartial
        };

        foreach (ProviderResultRecord providerResult in record.Results) {
            // Try to parse provider using consistent logic, skip if unknown
            if (!TryParseProvider( providerResult.Provider, out SupportedProviders provider )) {
                continue;
            }

            result.Results.Add( provider, new MusicLookupResult {
                Artist = providerResult.Artist,
                Title = providerResult.Title,
                ExternalId = providerResult.ExternalId ?? string.Empty,
                URL = providerResult.Url,
                ArtUrl = providerResult.ArtUrl ?? string.Empty,
                MarketRegion = providerResult.MarketRegion,
                IsAlbum = providerResult.IsAlbum
            } );
        }

        // Return null if no valid providers were parsed - prevents downstream errors
        if (result.Results.Count == 0) {
            return null;
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
            string description = GetEnumDescription( p );
            dict[description] = p;

            // Add description without spaces (e.g., "AppleMusic")
            string descriptionNoSpaces = description.Replace( " ", "", StringComparison.Ordinal );
            if (descriptionNoSpaces != description) {
                dict[descriptionNoSpaces] = p;
            }
        }

        return dict;
    }

    /// <summary>
    /// Gets the Description attribute value for an enum value, or the enum's string representation.
    /// </summary>
    private static string GetEnumDescription<T>( T enumValue ) where T : struct, Enum {
        string? value = enumValue.ToString( );
        if (value != null) {
            MemberInfo[] memberInfo = typeof( T ).GetMember( value );
            if (memberInfo.Length > 0) {
                object[] attrs = memberInfo[0].GetCustomAttributes( typeof( DescriptionAttribute ), false );
                if (attrs.Length > 0) {
                    return ((DescriptionAttribute)attrs[0]).Description;
                }
            }
            return value;
        }
        return string.Empty;
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

    /// <inheritdoc/>
    public async IAsyncEnumerable<(string AtUri, MediaLinkResult Result)> ListAllRecordsAsync(
        Uri pdsUri,
        string userDid,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        // Validate DID format before using it
        ATProtoUriHelper.ValidateDid( userDid, nameof( userDid ) );

        // Fetch all records via a single CAR download. Failures propagate so callers
        // can preserve the last known good cache rather than caching a zero-record result.
        List<(string AtUri, MediaLinkResult Result)> records =
            await FetchAllViaCarAsync( pdsUri, userDid, cancellationToken );

        foreach ((string atUri, MediaLinkResult result) in records) {
            cancellationToken.ThrowIfCancellationRequested( );
            yield return (atUri, result);
        }
    }

    private const long MaxCarBytes = 512L * 1024 * 1024;
    private const int ChunkSize = 81920; // 80 KB read buffer

    /// <summary>
    /// Downloads the repo as a CAR v1 file and enumerates all link.bridgebeats.lookup records.
    /// Per-record parse failures are skipped with a warning.
    /// Download or structural parse failures throw (HTTP or CarParseException).
    /// </summary>
    private async Task<List<(string AtUri, MediaLinkResult Result)>> FetchAllViaCarAsync(
        Uri pdsUri,
        string userDid,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested( );

        Stopwatch stopwatch = Stopwatch.StartNew( );
        string getRepoUrl = $"{pdsUri.ToString( ).TrimEnd( '/' )}/xrpc/com.atproto.sync.getRepo?did={Uri.EscapeDataString( userDid )}";

        HttpClient httpClient = httpClientFactory.CreateClient( ATProtoSyncHttpClientName );

        ReadOnlyMemory<byte> carMemory;
        try {
            using HttpResponseMessage response = await httpClient.GetAsync(
                getRepoUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken );

            _ = response.EnsureSuccessStatusCode( );

            // Reject early if Content-Length header is present and already exceeds cap
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxCarBytes) {
                throw new CarParseException(
                    $"CAR Content-Length {contentLength.Value} exceeds 512 MB cap." );
            }

            // Buffer the full CAR into memory — MST walk requires random access to blocks.
            // Copy in chunks enforcing the running total against the cap.
            using MemoryStream ms = new( );
            byte[] buffer = new byte[ChunkSize];
            Stream contentStream = await response.Content.ReadAsStreamAsync( cancellationToken );
            int read;
            while ((read = await contentStream.ReadAsync( buffer, cancellationToken )) > 0) {
                if (ms.Length + read > MaxCarBytes) {
                    throw new CarParseException(
                        $"CAR response exceeds 512 MB cap during download (already read {ms.Length + read} bytes)." );
                }
                ms.Write( buffer, 0, read );
            }

            carMemory = ms.GetBuffer( ).AsMemory( 0, (int)ms.Length );
        } catch (CarParseException ex) {
            LogCarDownloadFailed( logger, ex );
            throw;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            LogCarDownloadFailed( logger, ex );
            throw;
        }

        stopwatch.Stop( );
        long elapsedMs = stopwatch.ElapsedMilliseconds;

        List<(string AtUri, MediaLinkResult Result)> results = [];

        try {
            CarEnumerationResult enumResult =
                CarRepoReader.EnumerateCollection( carMemory, s_mediaLinkResultCollection.ToString( ), cancellationToken );

            if (enumResult.CommitVersion != 3) {
                LogCarCommitVersionUnexpected( logger, enumResult.CommitVersion );
            }

            int recordCount = 0;
            int skippedCount = 0;

            foreach ((string rkey, System.Text.Json.Nodes.JsonNode recordNode) in enumResult.Records) {
                cancellationToken.ThrowIfCancellationRequested( );

                // SEC-005: validate rkey before building AT-URI or logging it verbatim.
                // MST keys come from an operator-controlled PDS; an attacker could inject
                // control characters or path separators. Skip invalid rkeys with a warning.
                if (!IsValidRkey( rkey )) {
                    LogCarRecordSkipped( logger, TruncateForLog( rkey, 64 ), "invalid rkey" );
                    skippedCount++;
                    continue;
                }

                // Strip $type if present to avoid friction with JsonSerializer
                if (recordNode is System.Text.Json.Nodes.JsonObject obj && obj.ContainsKey( "$type" )) {
                    _ = obj.Remove( "$type" );
                }

                try {
                    string json = recordNode.ToJsonString( );
                    MediaLinkResultRecord? record = JsonSerializer.Deserialize<MediaLinkResultRecord>( json );

                    if (record is null) {
                        LogCarRecordSkipped( logger, rkey, "deserialized to null" );
                        skippedCount++;
                        continue;
                    }

                    MediaLinkResult? converted = ConvertFromRecord( record );
                    if (converted is null) {
                        LogCarRecordSkipped( logger, rkey, "no valid providers" );
                        skippedCount++;
                        continue;
                    }

                    string atUri = ATProtoUriHelper.BuildLookupRecordUri( userDid, rkey );
                    results.Add( (atUri, converted) );
                    recordCount++;
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    LogCarRecordSkipped( logger, rkey, ex.Message.SanitizeForLogging( ) );
                    skippedCount++;
                }
            }

            LogCarDownloaded( logger, carMemory.Length, enumResult.BlockCount, elapsedMs );
            LogCarEnumerated( logger, recordCount, skippedCount );
        } catch (CarParseException ex) {
            LogCarDownloadFailed( logger, ex );
            throw;
        } catch (OperationCanceledException) {
            throw;
        }

        return results;
    }

    #region Rkey validation

    /// <summary>
    /// Maximum record-key length per the atproto record-key specification.
    /// </summary>
    private const int MaxRkeyLength = 512;

    /// <summary>
    /// Validates an atproto record key against the allowed grammar.
    /// Valid characters: A-Za-z0-9 . _ ~ : -
    /// Length must be between 1 and 512 characters.
    /// </summary>
    private static bool IsValidRkey( string rkey ) {
        if (rkey.Length == 0 || rkey.Length > MaxRkeyLength) {
            return false;
        }

        foreach (char c in rkey) {
            if (!char.IsAsciiLetterOrDigit( c )
                && c != '.' && c != '_' && c != '~' && c != ':' && c != '-') {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the first <paramref name="maxLen"/> characters of <paramref name="value"/>,
    /// appending "..." if truncated, and replaces ASCII control characters (\r, \n, \t and
    /// other C0 codes) with spaces to prevent CWE-117 log-forging on plain-text sinks.
    /// </summary>
    private static string TruncateForLog( string value, int maxLen ) {
        ReadOnlySpan<char> source = value.Length <= maxLen
            ? value.AsSpan( )
            : value.AsSpan( 0, maxLen );
        bool hasControl = false;
        foreach (char c in source) {
            if (c < ' ' || c == 127) { hasControl = true; break; }
        }
        if (!hasControl) {
            return value.Length <= maxLen ? value : string.Concat( source, "..." );
        }
        System.Text.StringBuilder sb = new( source.Length + 3 );
        foreach (char c in source) {
            _ = sb.Append( c < ' ' || c == 127 ? ' ' : c );
        }
        if (value.Length > maxLen) {
            _ = sb.Append( "..." );
        }
        return sb.ToString( );
    }

    #endregion

    #region LoggerMessage Methods

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceUpdatedRecord,
        Level = LogLevel.Information,
        Message = "Successfully updated existing MediaLinkResult on ATProto PDS: {uri}" )]
    internal static partial void LogUpdatedRecord( ILogger logger, string uri );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceCreatedRecord,
        Level = LogLevel.Information,
        Message = "Successfully created MediaLinkResult on ATProto PDS: {uri}" )]
    internal static partial void LogCreatedRecord( ILogger logger, string uri );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceStoreError,
        Level = LogLevel.Error,
        Message = "Failed to store MediaLinkResult on ATProto PDS" )]
    internal static partial void LogStoreError( ILogger logger, Exception ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceGetRecordFailed,
        Level = LogLevel.Warning,
        Message = "Failed to get record from ATProto PDS: {error}" )]
    internal static partial void LogGetRecordFailed( ILogger logger, string error );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceRetrieveError,
        Level = LogLevel.Error,
        Message = "Failed to retrieve MediaLinkResult from ATProto PDS: {uri}" )]
    internal static partial void LogRetrieveError( ILogger logger, Exception ex, string uri );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceCarDownloaded,
        Level = LogLevel.Information,
        Message = "CAR download complete: {Bytes} bytes, {Blocks} blocks, {ElapsedMs} ms" )]
    internal static partial void LogCarDownloaded( ILogger logger, long bytes, int blocks, long elapsedMs );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceCarEnumerated,
        Level = LogLevel.Information,
        Message = "CAR enumeration complete: {RecordCount} records returned, {SkippedCount} skipped" )]
    internal static partial void LogCarEnumerated( ILogger logger, int recordCount, int skippedCount );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceCarCommitVersionUnexpected,
        Level = LogLevel.Warning,
        Message = "CAR commit version {CommitVersion} is not 3; proceeding anyway" )]
    internal static partial void LogCarCommitVersionUnexpected( ILogger logger, int commitVersion );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceCarDownloadFailed,
        Level = LogLevel.Error,
        Message = "CAR download or parse failed" )]
    internal static partial void LogCarDownloadFailed( ILogger logger, Exception? ex );

    [LoggerMessage(
        EventId = LogEventIds.Infrastructure.Storage.ATProtoStorageServiceCarRecordSkipped,
        Level = LogLevel.Warning,
        Message = "Skipping record {Rkey}: {Reason}" )]
    internal static partial void LogCarRecordSkipped( ILogger logger, string rkey, string reason );

    #endregion
}
