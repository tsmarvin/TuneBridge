using System.Security.Cryptography;
using System.Text;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Core.Infrastructure.Playlists;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Domain.Services.Cards {

    /// <summary>
    /// Database-backed implementation of the playlist service.
    /// </summary>
    public class PlaylistService( string baseUrl, IDbContextFactory<ApplicationDbContext> contextFactory ) : IPlaylistService {

        /// <inheritdoc/>
        public bool IsEnabled => !string.IsNullOrWhiteSpace( baseUrl );

        /// <inheritdoc/>
        public string BaseUrl => baseUrl;

        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory = contextFactory;
        private const int MaxPlaylistSize = 20;
        private static readonly TimeSpan AnonymousExpiration = TimeSpan.FromDays( 14 ); // 2 weeks for anonymous

        /// <inheritdoc/>
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
                    ExpiresAt = string.IsNullOrWhiteSpace( userId ) ? DateTime.UtcNow.Add( AnonymousExpiration ) : null
                };

                _ = context.Playlists.Add( entry );
                _ = await context.SaveChangesAsync( );
            }

            // Generate the playlist card URL
            return $"https://{baseUrl.TrimEnd( '/' )}/playlist/{playlistId}";
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public async Task<List<PlaylistEntryDto>> GetUserPlaylistsAsync( string userId ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            List<PlaylistEntry> entries = await context.Playlists
                .Where( p => p.UserId == userId )
                .OrderByDescending( p => p.CreatedAt )
                .ToListAsync( );

            return entries.ConvertAll( ToDto );
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public async Task<List<PlaylistEntryDto>> ExportUserDataAsync( string userId ) {
            using ApplicationDbContext context = await _contextFactory.CreateDbContextAsync( );

            List<PlaylistEntry> entries = await context.Playlists
                .Where( p => p.UserId == userId )
                .OrderByDescending( p => p.CreatedAt )
                .ToListAsync( );

            return entries.ConvertAll( ToDto );
        }

        /// <inheritdoc/>
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

        /// <summary>
        /// Converts a PlaylistEntry entity to a PlaylistEntryDto.
        /// </summary>
        /// <param name="entry">The EF entity to convert.</param>
        /// <returns>The DTO representation.</returns>
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
        /// Generates a deterministic playlist ID based on the ordered card IDs.
        /// Uses SHA-256 hash of the concatenated card IDs and converts to base32 for URL safety.
        /// </summary>
        /// <param name="cardIds">Ordered list of card IDs.</param>
        /// <returns>A deterministic, URL-safe playlist ID.</returns>
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
        /// Converts a byte array to a base32 string using RFC 4648 alphabet.
        /// </summary>
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
