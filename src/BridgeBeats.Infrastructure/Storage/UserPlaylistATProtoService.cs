using System.Runtime.CompilerServices;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Infrastructure.Identity;
using idunno.AtProto;
using idunno.AtProto.Repo;
using idunno.Bluesky;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Infrastructure.Storage;

/// <summary>
/// Service for managing user playlists on their ATProto PDS using OAuth authentication.
/// This service writes to individual user PDSs (not the server's PDS), requiring
/// user-specific OAuth tokens obtained through ATProto OAuth login.
/// </summary>
/// <remarks>
/// This service is separate from <see cref="ATProtoStorageService"/> because it operates
/// on user PDSs rather than the server's PDS, and uses OAuth tokens
/// instead of app passwords.
/// </remarks>
public class UserPlaylistATProtoService : IUserPlaylistATProtoService {

    /// <summary>
    /// The NSID (Namespaced Identifier) for the BridgeBeats playlist lexicon.
    /// </summary>
    private static readonly Nsid s_playlistCollection = new( "link.bridgebeats.playlist" );

    private readonly string _serverDid;
    private readonly ILogger<UserPlaylistATProtoService> _logger;
    private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
    private readonly IATProtoOAuthService? _oauthService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPlaylistATProtoService"/> class.
    /// </summary>
    /// <param name="serverDid">The DID of the BridgeBeats server (used as default lookupRepository).</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="dbContextFactory">Factory for creating database contexts.</param>
    /// <param name="oauthService">Optional ATProto OAuth service for token refresh.</param>
    public UserPlaylistATProtoService(
        string serverDid,
        ILogger<UserPlaylistATProtoService> logger,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        IATProtoOAuthService? oauthService = null
    ) {
        _serverDid = serverDid ?? throw new ArgumentNullException( nameof( serverDid ) );
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException( nameof( dbContextFactory ) );
        _oauthService = oauthService;
    }

    /// <inheritdoc/>
    public async Task<string> CreatePlaylistAsync(
        string userDid,
        ATProtoPlaylistDto playlist,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( userDid );
        ArgumentNullException.ThrowIfNull( playlist );

        // TODO: Replace with OAuth-authenticated agent when #210 is implemented
        // For now, this will use unauthenticated access which will fail for writes.
        // The actual implementation will need to retrieve the user's OAuth session.
        BlueskyAgent agent = await GetAuthenticatedAgentForUserAsync( userDid, cancellationToken );

        try {
            // Generate TID-based record key for chronological sorting
            RecordKey recordKey = TimestampIdentifier.Next( );

            // Ensure the lookup repository is set (default to server DID)
            playlist.LookupRepository = string.IsNullOrEmpty( playlist.LookupRepository )
                ? _serverDid
                : playlist.LookupRepository;

            // Ensure createdBy is set
            playlist.CreatedBy = string.IsNullOrEmpty( playlist.CreatedBy )
                ? userDid
                : playlist.CreatedBy;

            // Set updatedAt to current UTC time
            playlist.UpdatedAt = DateTimeOffset.UtcNow;

            // Convert DTO to record
            PlaylistRecord record = ConvertToRecord( playlist );

            AtProtoHttpResult<CreateRecordResult> createResult = await agent.CreateRecord(
                record: record,
                collection: s_playlistCollection,
                rKey: recordKey,
                validate: false,
                cancellationToken: cancellationToken
            );

            if (!createResult.Succeeded || createResult.Result is null) {
                string errorMsg = createResult.AtErrorDetail?.Message ?? $"HTTP {createResult.StatusCode}";
                throw new InvalidOperationException( $"Failed to create playlist on user's PDS: {errorMsg}" );
            }

            _logger.LogInformation(
                "Successfully created playlist '{Title}' for user {UserDid}: {Uri}",
                playlist.Title,
                userDid,
                createResult.Result.Uri
            );

            return createResult.Result.Uri.ToString( );
        } catch (Exception ex) when (ex is not InvalidOperationException) {
            _logger.LogError( ex, "Failed to create playlist for user {UserDid}", userDid );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ATProtoPlaylistDto?> GetPlaylistAsync(
        string playlistUri,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( playlistUri );

        // Use unauthenticated agent for reading public records
        BlueskyAgent agent = new( );

        try {
            AtUri atUri = new( playlistUri );

            AtProtoHttpResult<AtProtoRepositoryRecord<PlaylistRecord>> getResult = await agent.GetRecord<PlaylistRecord>(
                uri: atUri,
                cid: null,
                cancellationToken: cancellationToken
            );

            if (!getResult.Succeeded || getResult.Result?.Value is null) {
                if (!getResult.Succeeded) {
                    string errorMsg = getResult.AtErrorDetail?.Message ?? $"HTTP {getResult.StatusCode}";
                    _logger.LogWarning( "Failed to get playlist from PDS: {Error}", errorMsg );
                }
                return null;
            }

            ATProtoPlaylistDto dto = ConvertFromRecord( getResult.Result!.Value );
            dto.AtUri = playlistUri;
            dto.Rkey = atUri.RecordKey?.Value;

            return dto;
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to retrieve playlist from PDS: {Uri}", playlistUri );
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string> UpdatePlaylistAsync(
        string userDid,
        string rkey,
        ATProtoPlaylistDto playlist,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( userDid );
        ArgumentException.ThrowIfNullOrWhiteSpace( rkey );
        ArgumentNullException.ThrowIfNull( playlist );

        // TODO: Replace with OAuth-authenticated agent when #210 is implemented
        BlueskyAgent agent = await GetAuthenticatedAgentForUserAsync( userDid, cancellationToken );

        try {
            // Ensure the lookup repository is set (default to server DID)
            playlist.LookupRepository = string.IsNullOrEmpty( playlist.LookupRepository )
                ? _serverDid
                : playlist.LookupRepository;

            // Set updatedAt to current UTC time
            playlist.UpdatedAt = DateTimeOffset.UtcNow;

            // Convert DTO to record
            PlaylistRecord record = ConvertToRecord( playlist );

            RecordKey recordKey = new( rkey );

            AtProtoHttpResult<PutRecordResult> putResult = await agent.PutRecord(
                record: record,
                collection: s_playlistCollection,
                rKey: recordKey,
                validate: false,
                cancellationToken: cancellationToken
            );

            if (!putResult.Succeeded || putResult.Result is null) {
                string errorMsg = putResult.AtErrorDetail?.Message ?? $"HTTP {putResult.StatusCode}";
                throw new InvalidOperationException( $"Failed to update playlist on user's PDS: {errorMsg}" );
            }

            _logger.LogInformation(
                "Successfully updated playlist '{Title}' for user {UserDid}: {Uri}",
                playlist.Title,
                userDid,
                putResult.Result.Uri
            );

            return putResult.Result.Uri.ToString( );
        } catch (Exception ex) when (ex is not InvalidOperationException) {
            _logger.LogError( ex, "Failed to update playlist for user {UserDid}", userDid );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeletePlaylistAsync(
        string userDid,
        string rkey,
        CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( userDid );
        ArgumentException.ThrowIfNullOrWhiteSpace( rkey );

        // TODO: Replace with OAuth-authenticated agent when #210 is implemented
        BlueskyAgent agent = await GetAuthenticatedAgentForUserAsync( userDid, cancellationToken );

        try {
            Did repo = new( userDid );
            RecordKey recordKey = new( rkey );

            AtProtoHttpResult<Commit> deleteResult = await agent.DeleteRecord(
                repo: repo,
                collection: s_playlistCollection,
                rKey: recordKey,
                cancellationToken: cancellationToken
            );

            if (!deleteResult.Succeeded) {
                string errorMsg = deleteResult.AtErrorDetail?.Message ?? $"HTTP {deleteResult.StatusCode}";
                _logger.LogWarning( "Failed to delete playlist for user {UserDid}: {Error}", userDid, errorMsg );
                return false;
            }

            _logger.LogInformation( "Successfully deleted playlist {Rkey} for user {UserDid}", rkey, userDid );
            return true;
        } catch (Exception ex) {
            _logger.LogError( ex, "Failed to delete playlist {Rkey} for user {UserDid}", rkey, userDid );
            return false;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ATProtoPlaylistDto> ListUserPlaylistsAsync(
        string userDid,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace( userDid );

        // Use unauthenticated agent for listing public records
        BlueskyAgent agent = new( );
        Did repo = new( userDid );
        string? cursor = null;

        do {
            cancellationToken.ThrowIfCancellationRequested( );

            AtProtoHttpResult<PagedReadOnlyCollection<AtProtoRepositoryRecord<PlaylistRecord>>> listResult =
                await agent.ListRecords<PlaylistRecord>(
                    repo: repo,
                    collection: s_playlistCollection,
                    limit: 100,
                    cursor: cursor,
                    cancellationToken: cancellationToken
                );

            if (!listResult.Succeeded || listResult.Result is null) {
                string errorMsg = listResult.AtErrorDetail?.Message ?? $"HTTP {listResult.StatusCode}";
                _logger.LogError( "Failed to list playlists for user {UserDid}: {Error}", userDid, errorMsg );
                yield break;
            }

            foreach (AtProtoRepositoryRecord<PlaylistRecord> record in listResult.Result) {
                if (record.Value is not null) {
                    ATProtoPlaylistDto dto = ConvertFromRecord( record.Value );
                    dto.AtUri = record.Uri.ToString( );
                    dto.Rkey = record.Uri.RecordKey?.Value;
                    yield return dto;
                }
            }

            cursor = listResult.Result.Cursor;
        } while (!string.IsNullOrEmpty( cursor ));
    }

    /// <summary>
    /// Gets an authenticated BlueskyAgent for the specified user.
    /// </summary>
    /// <remarks>
    /// Retrieves the user's OAuth tokens from the identity store and creates
    /// an authenticated agent. Handles token refresh if needed.
    /// </remarks>
    /// <param name="userDid">The DID of the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An authenticated BlueskyAgent for the user.</returns>
    /// <exception cref="InvalidOperationException">Thrown when user is not found or has no valid OAuth tokens.</exception>
    private async Task<BlueskyAgent> GetAuthenticatedAgentForUserAsync(
        string userDid,
        CancellationToken cancellationToken = default
    ) {
        await using ApplicationDbContext dbContext = await _dbContextFactory.CreateDbContextAsync( cancellationToken );

        // Find the user by their ATProto DID
        ApplicationUser? user = await dbContext.Users
            .FirstOrDefaultAsync( u => u.AtProtoDid == userDid, cancellationToken );

        if (user is null) {
            throw new InvalidOperationException(
                $"User with ATProto DID '{userDid}' not found. User must be logged in with ATProto OAuth."
            );
        }

        // Check if user has OAuth tokens
        if (string.IsNullOrEmpty( user.AtProtoAccessToken ) ||
            string.IsNullOrEmpty( user.AtProtoRefreshToken ) ||
            string.IsNullOrEmpty( user.EncryptedAtProtoDPoPKey )) {
            throw new InvalidOperationException(
                $"User '{userDid}' does not have valid ATProto OAuth tokens. Please log in with Bluesky."
            );
        }

        // Check if tokens need refresh
        if (_oauthService is not null && !_oauthService.IsTokenValid( user.AtProtoTokenExpiration )) {
            _logger.LogDebug( "ATProto tokens expired for user {UserDid}, attempting refresh", userDid );

            ATProtoOAuthResult? refreshResult = await _oauthService.RefreshTokensAsync(
                user.AtProtoDid!,
                user.AtProtoRefreshToken,
                user.EncryptedAtProtoDPoPKey,
                cancellationToken
            );

            if (refreshResult is not null) {
                // Update the stored tokens
                user.AtProtoAccessToken = refreshResult.AccessToken;
                user.AtProtoRefreshToken = refreshResult.RefreshToken;
                user.EncryptedAtProtoDPoPKey = refreshResult.DPoPKeyJwk;
                user.AtProtoTokenExpiration = refreshResult.TokenExpiration;
                _ = await dbContext.SaveChangesAsync( cancellationToken );

                _logger.LogInformation( "Successfully refreshed ATProto tokens for user {UserDid}", userDid );
            } else {
                throw new InvalidOperationException(
                    $"Failed to refresh ATProto tokens for user '{userDid}'. Please log in again with Bluesky."
                );
            }
        }

        // Create an authenticated agent
        // Note: The idunno.Bluesky library doesn't currently support creating an agent with
        // pre-existing OAuth tokens. This functionality is blocked until the library provides
        // a way to restore OAuth sessions or until we implement direct HTTP calls to the PDS.
        //
        // Required for implementation:
        // 1. Retrieve the DPoP key from user.EncryptedAtProtoDPoPKey
        //    (WARNING: Currently stored as plain text - encryption not yet implemented)
        // 2. Create an agent with the stored session (access token, refresh token, DPoP key)
        // 3. Use the DPoP key to sign authenticated requests to the PDS
        //
        // Tracked in issue: https://github.com/tsmarvin/BridgeBeats/issues/210

        throw new NotImplementedException(
            "ATProto OAuth session restoration is not yet implemented. " +
            "The idunno.Bluesky library does not support creating authenticated agents from stored OAuth credentials. " +
            "This functionality is required for playlist write operations. Tracked in issue #210."
        );
    }

    /// <summary>
    /// Converts an ATProtoPlaylistDto to a PlaylistRecord for storage.
    /// </summary>
    private static PlaylistRecord ConvertToRecord( ATProtoPlaylistDto dto ) {
        PlaylistSubstitutionMap? substitutions = null;

        if (dto.Substitutions is { Count: > 0 }) {
            substitutions = new PlaylistSubstitutionMap(
                appleMusic: dto.Substitutions.TryGetValue( "applemusic", out Dictionary<string, string>? apple ) ? apple : null,
                spotify: dto.Substitutions.TryGetValue( "spotify", out Dictionary<string, string>? spotify ) ? spotify : null,
                tidal: dto.Substitutions.TryGetValue( "tidal", out Dictionary<string, string>? tidal ) ? tidal : null
            );
        }

        return new PlaylistRecord(
            title: dto.Title,
            createdBy: dto.CreatedBy,
            updatedAt: dto.UpdatedAt,
            lookupRepository: dto.LookupRepository,
            tracks: dto.Tracks.ToList( ),
            description: dto.Description,
            substitutions: substitutions
        );
    }

    /// <summary>
    /// Converts a PlaylistRecord from storage to an ATProtoPlaylistDto.
    /// </summary>
    private static ATProtoPlaylistDto ConvertFromRecord( PlaylistRecord record ) {
        Dictionary<string, Dictionary<string, string>>? substitutions = null;

        if (record.Substitutions?.HasAnySubstitutions == true) {
            substitutions = [];

            if (record.Substitutions.AppleMusic is { Count: > 0 }) {
                substitutions["applemusic"] = new Dictionary<string, string>( record.Substitutions.AppleMusic );
            }

            if (record.Substitutions.Spotify is { Count: > 0 }) {
                substitutions["spotify"] = new Dictionary<string, string>( record.Substitutions.Spotify );
            }

            if (record.Substitutions.Tidal is { Count: > 0 }) {
                substitutions["tidal"] = new Dictionary<string, string>( record.Substitutions.Tidal );
            }
        }

        return new ATProtoPlaylistDto {
            Title = record.Title,
            Description = record.Description,
            CreatedBy = record.CreatedBy,
            UpdatedAt = record.UpdatedAt,
            LookupRepository = record.LookupRepository,
            Tracks = record.Tracks.ToList( ),
            Substitutions = substitutions
        };
    }
}
