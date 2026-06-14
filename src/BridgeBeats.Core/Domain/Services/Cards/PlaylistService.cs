using System.Security.Cryptography;
using System.Text;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Core.Infrastructure.Playlists;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Domain.Services.Cards {

    /// <summary>
    /// Persists user and anonymous playlists of cards to the EF Core
    /// <see cref="ApplicationDbContext.Playlists"/> table. Playlist ids are deterministic (derived
    /// from the card ids), so re-creating a playlist with the same cards updates the existing row.
    /// Anonymous playlists expire after 14 days; assigning a user id on creation claims the
    /// playlist and clears its expiry.
    /// </summary>
    /// <param name="domain">
    /// The public domain used to build playlist URLs. When null/empty the service is disabled
    /// (see <see cref="IsEnabled"/>).
    /// </param>
    /// <param name="contextFactory">Factory used to create a fresh <see cref="ApplicationDbContext"/> per operation.</param>
    public class PlaylistService( string domain, IDbContextFactory<ApplicationDbContext> contextFactory ) : IPlaylistService {

        /// <summary>Gets a value indicating whether the service is enabled, that is, whether a public domain is configured.</summary>
        public bool IsEnabled => !string.IsNullOrWhiteSpace( domain );

        /// <summary>Gets the public domain used to build playlist URLs.</summary>
        public string Domain => domain;

        /// <summary>Factory used to create a short-lived database context for each operation.</summary>
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory = contextFactory;
        /// <summary>The maximum number of cards a single playlist may contain.</summary>
        private const int MaxPlaylistSize = 20;
        /// <summary>The lifetime of an anonymous (unclaimed) playlist before it becomes eligible for cleanup. 14 days.</summary>
        private static readonly TimeSpan s_anonymousExpiration = TimeSpan.FromDays( 14 ); // 2 weeks for anonymous

        /// <summary>
        /// Creates a playlist, or updates the existing one when a playlist with the same
        /// deterministic id already exists. The id is derived from the ordered card ids, so the same
        /// set of cards always maps to the same playlist. When an existing anonymous playlist is
        /// claimed by passing a <paramref name="userId"/>, its expiry is cleared.
        /// </summary>
        /// <param name="cardIds">The ordered card ids that make up the playlist. Must be non-empty and at most <see cref="MaxPlaylistSize"/> entries.</param>
        /// <param name="cardRkeys">The ATProto record keys for the cards; must have the same count as <paramref name="cardIds"/>.</param>
        /// <param name="title">Optional playlist title; applied only when non-empty.</param>
        /// <param name="description">Optional playlist description; applied only when non-empty.</param>
        /// <param name="userId">
        /// Optional owning user id. When supplied for a new or unclaimed playlist, the playlist is
        /// owned by that user and never expires; when omitted, the playlist is anonymous and expires
        /// after 14 days.
        /// </param>
        /// <returns>The public <c>https://{domain}/playlist/{playlistId}</c> URL for the playlist.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="cardIds"/> is null or empty, exceeds <see cref="MaxPlaylistSize"/>,
        /// or when <paramref name="cardRkeys"/> is null or its count does not match <paramref name="cardIds"/>.
        /// </exception>
        public async Task<string> CreatePlaylistAsync( List<string> cardIds, List<string> cardRkeys, string? title = null, string? description = null, string? userId = null ) {
            if (cardIds == null || cardIds.Count == 0) {
                throw new ArgumentException( "Card IDs list cannot be null or empty", nameof( cardIds ) );
            }

            if (cardIds.Count > MaxPlaylistSize) {
                throw new ArgumentException( $"Playlist cannot contain more than {MaxPlaylistSize} cards", nameof( cardIds ) );
            }

            if (cardRkeys == null || cardRkeys.Count != cardIds.Count) {
                throw new ArgumentException( "Card rkeys list must match card IDs count", nameof( cardRkeys ) );
            }

            // Generate deterministic playlist ID based on ordered card IDs
            string playlistId = GeneratePlaylistId( cardIds );

            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            // Check if playlist already exists
            PlaylistEntry? existing = await context.Playlists
                .FirstOrDefaultAsync( p => p.PlaylistId == playlistId );

            if (existing != null) {
                // Update title/description if provided
                if (!string.IsNullOrWhiteSpace( title )) {
                    existing.Title = title;
                }
                if (!string.IsNullOrWhiteSpace( description )) {
                    existing.Description = description;
                }

                // Update rkeys in case they've changed
                existing.CardRkeys = string.Join( ",", cardRkeys );

                // If user is logged in and playlist was anonymous, take ownership
                if (!string.IsNullOrWhiteSpace( userId ) && string.IsNullOrWhiteSpace( existing.UserId )) {
                    existing.UserId = userId;
                    existing.ExpiresAt = null; // Remove expiration for logged-in users
                }

                _ = await context.SaveChangesAsync( );
            } else {
                // Create new playlist
                PlaylistEntry entry = new( ) {
                    PlaylistId = playlistId,
                    UserId = userId,
                    Title = title,
                    Description = description,
                    CardIds = string.Join( ",", cardIds ),
                    CardRkeys = string.Join( ",", cardRkeys ),
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = string.IsNullOrWhiteSpace( userId ) ? DateTime.UtcNow.Add( s_anonymousExpiration ) : null
                };

                _ = context.Playlists.Add( entry );
                _ = await context.SaveChangesAsync( );
            }

            // Generate the playlist card URL
            return $"https://{domain.TrimEnd( '/' )}/playlist/{playlistId}";
        }

        /// <summary>
        /// Retrieves a playlist by id. If the playlist has passed its expiry it is deleted and
        /// <see langword="null"/> is returned (expired playlists are treated as gone).
        /// </summary>
        /// <param name="playlistId">The deterministic playlist id.</param>
        /// <returns>The playlist as a DTO, or <see langword="null"/> when not found or expired.</returns>
        public async Task<PlaylistEntryDto?> GetPlaylistAsync( string playlistId ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            PlaylistEntry? playlist = await context.Playlists
                .FirstOrDefaultAsync( p => p.PlaylistId == playlistId );

            if (playlist == null) {
                return null;
            }

            // Check if expired (only for anonymous playlists)
            if (playlist.ExpiresAt.HasValue && playlist.ExpiresAt.Value <= DateTime.UtcNow) {
                _ = context.Playlists.Remove( playlist );
                _ = await context.SaveChangesAsync( );
                return null;
            }

            return ToDto( playlist );
        }

        /// <summary>Retrieves all playlists owned by a user, newest first.</summary>
        /// <param name="userId">The owning user id.</param>
        /// <returns>The user's playlists as DTOs, ordered by creation time descending.</returns>
        public async Task<List<PlaylistEntryDto>> GetUserPlaylistsAsync( string userId ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            List<PlaylistEntry> entries = await context.Playlists
                .Where( p => p.UserId == userId )
                .OrderByDescending( p => p.CreatedAt )
                .ToListAsync( );

            return entries.ConvertAll( ToDto );
        }

        /// <summary>
        /// Removes every playlist whose expiry has passed. Called periodically by
        /// <see cref="PlaylistCleanupService"/>. Only anonymous (unclaimed) playlists carry an
        /// expiry, so claimed playlists are never removed here.
        /// </summary>
        /// <returns>A task that completes when expired playlists have been deleted.</returns>
        public async Task CleanExpiredPlaylistsAsync( ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            DateTime now = DateTime.UtcNow;
            List<PlaylistEntry> expired = await context.Playlists
                .Where( p => p.ExpiresAt.HasValue && p.ExpiresAt.Value <= now )
                .ToListAsync( );

            if (expired.Count > 0) {
                context.Playlists.RemoveRange( expired );
                _ = await context.SaveChangesAsync( );
            }
        }

        /// <summary>
        /// Exports all of a user's playlists, for data-portability and account-export purposes.
        /// Returns the same data as <see cref="GetUserPlaylistsAsync"/>.
        /// </summary>
        /// <param name="userId">The user whose data is exported.</param>
        /// <returns>The user's playlists as DTOs, ordered by creation time descending.</returns>
        public async Task<List<PlaylistEntryDto>> ExportUserDataAsync( string userId ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            List<PlaylistEntry> entries = await context.Playlists
                .Where( p => p.UserId == userId )
                .OrderByDescending( p => p.CreatedAt )
                .ToListAsync( );

            return entries.ConvertAll( ToDto );
        }

        /// <summary>
        /// Deletes a playlist owned by the given user. The user id is part of the match, so a user
        /// can only delete their own playlists.
        /// </summary>
        /// <param name="playlistId">The deterministic playlist id.</param>
        /// <param name="userId">The owning user id; must match the playlist's owner.</param>
        /// <returns><see langword="true"/> if a matching playlist was deleted; otherwise <see langword="false"/>.</returns>
        public async Task<bool> DeletePlaylistAsync( string playlistId, string userId ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            PlaylistEntry? playlist = await context.Playlists
                .FirstOrDefaultAsync( p => p.PlaylistId == playlistId && p.UserId == userId );

            if (playlist == null) {
                return false;
            }

            _ = context.Playlists.Remove( playlist );
            _ = await context.SaveChangesAsync( );
            return true;
        }

        /// <summary>Maps a persisted <see cref="PlaylistEntry"/> row to its transport DTO.</summary>
        /// <param name="entry">The persisted playlist row.</param>
        /// <returns>A <see cref="PlaylistEntryDto"/> mirroring the row's fields.</returns>
        private static PlaylistEntryDto ToDto( PlaylistEntry entry ) => new( ) {
            PlaylistId = entry.PlaylistId,
            UserId = entry.UserId,
            Title = entry.Title,
            Description = entry.Description,
            CardIds = entry.CardIds,
            CardRkeys = entry.CardRkeys,
            CreatedAt = entry.CreatedAt,
            ExpiresAt = entry.ExpiresAt
        };

        /// <summary>
        /// Builds the deterministic playlist id from the ordered card ids: the ids are joined,
        /// hashed with SHA-256, base32-encoded, lowercased, and truncated to at most 32 characters.
        /// The same set of cards in the same order always yields the same id.
        /// </summary>
        /// <param name="cardIds">The ordered card ids.</param>
        /// <returns>A stable, lowercase, base32 playlist id of up to 32 characters.</returns>
        private static string GeneratePlaylistId( List<string> cardIds ) {
            // Create deterministic string from ordered card IDs
            string concatenated = string.Join( "|", cardIds );

            // Compute SHA-256 hash
            byte[] hashBytes = SHA256.HashData( Encoding.UTF8.GetBytes( concatenated ) );

            // Convert to base32 (using RFC 4648 alphabet) and truncate to 32 characters
            string base32Hash = ToBase32( hashBytes );
            return base32Hash[..Math.Min( 32, base32Hash.Length )].ToLowerInvariant( );
        }

        /// <summary>
        /// Encodes a byte array as an RFC 4648 base32 string (alphabet <c>A-Z2-7</c>), without
        /// padding. Returns an empty string for null or empty input.
        /// </summary>
        /// <param name="input">The bytes to encode.</param>
        /// <returns>The base32 representation, with any trailing <c>=</c> padding removed.</returns>
        private static string ToBase32( byte[] input ) {
            if (input == null || input.Length == 0) {
                return string.Empty;
            }

            // Base32 alphabet (RFC 4648)
            const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            StringBuilder result = new( );
            int bits = 0;
            int value = 0;

            foreach (byte b in input) {
                value = (value << 8) | b;
                bits += 8;

                while (bits >= 5) {
                    bits -= 5;
                    int index = (value >> bits) & 0x1F;
                    _ = result.Append( Base32Alphabet[index] );
                }
            }

            if (bits > 0) {
                int index = (value << (5 - bits)) & 0x1F;
                _ = result.Append( Base32Alphabet[index] );
            }

            // Remove padding ('=') for URL safety
            return result.ToString( ).TrimEnd( '=' );
        }
    }
}
