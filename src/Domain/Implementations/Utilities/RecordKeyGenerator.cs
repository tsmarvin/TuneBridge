using System.Security.Cryptography;
using System.Text;
using TuneBridge.Domain.Contracts.DTOs;

namespace TuneBridge.Domain.Implementations.Utilities {

    /// <summary>
    /// Provides deterministic generation of ATProto record keys (rkeys) and card IDs
    /// based on media metadata to ensure consistent mapping and prevent duplication.
    /// </summary>
    public static class RecordKeyGenerator {

        /// <summary>
        /// Generates a deterministic rkey for a MediaLinkResult based on the first available
        /// externalId (ISRC/UPC) and media type from the results. If no externalId is found,
        /// generates a fallback rkey based on metadata hash.
        /// </summary>
        /// <param name="result">The MediaLinkResult containing provider results.</param>
        /// <returns>A deterministic rkey string.</returns>
        /// <remarks>
        /// Primary format: "track:{externalId}" or "album:{externalId}"
        /// Fallback format: "metadata:{hash}" where hash is base32(sha256(title|artist|isAlbum))
        /// The externalId is sanitized to be URL-safe (alphanumeric and hyphens only).
        /// </remarks>
        public static string GenerateRkey( MediaLinkResult result ) {
            if (result?.Results == null || result.Results.Count == 0) {
                throw new ArgumentException( "MediaLinkResult must contain at least one result", nameof( result ) );
            }

            // Find the first result with a non-empty externalId
            MusicLookupResultDto? firstResultWithId = result.Results.Values
                .FirstOrDefault( r => !string.IsNullOrWhiteSpace( r.ExternalId ) );

            if (firstResultWithId != null) {
                // Determine media type prefix
                string prefix = firstResultWithId.IsAlbum == true ? "album" : "track";

                // Sanitize the externalId to be URL-safe (alphanumeric and hyphens)
                string sanitizedId = SanitizeForRkey( firstResultWithId.ExternalId );

                if (!string.IsNullOrEmpty( sanitizedId )) {
                    return $"{prefix}:{sanitizedId}";
                }
            }

            // Fallback: Generate rkey from metadata hash
            return GenerateMetadataBasedRkey( result );
        }

        /// <summary>
        /// Generates a fallback rkey based on metadata when no externalId is available.
        /// </summary>
        private static string GenerateMetadataBasedRkey( MediaLinkResult result ) {
            // Get first result to extract metadata
            MusicLookupResultDto firstResult = result.Results.Values.First( );

            // Create a stable string from metadata
            string metadataString = $"{firstResult.Title}|{firstResult.Artist}|{(firstResult.IsAlbum == true ? "album" : "track")}";

            // Compute SHA-256 hash
            byte[] hashBytes = SHA256.HashData( Encoding.UTF8.GetBytes( metadataString ) );

            // Convert to base32 and truncate to reasonable length (16 chars for metadata-based keys)
            string base32Hash = ToBase32( hashBytes )[..16].ToLowerInvariant( );

            return $"metadata:{base32Hash}";
        }

        /// <summary>
        /// Generates a deterministic rkey from a specific externalId and media type.
        /// </summary>
        /// <param name="externalId">The ISRC or UPC identifier.</param>
        /// <param name="isAlbum">True for albums (UPC), false for tracks (ISRC).</param>
        /// <returns>A deterministic rkey string.</returns>
        public static string GenerateRkey( string externalId, bool isAlbum ) {
            if (string.IsNullOrWhiteSpace( externalId )) {
                throw new ArgumentException( "ExternalId cannot be null or empty", nameof( externalId ) );
            }

            string prefix = isAlbum ? "album" : "track";
            string sanitizedId = SanitizeForRkey( externalId );

            if (string.IsNullOrEmpty( sanitizedId )) {
                throw new ArgumentException( "ExternalId must contain valid characters after sanitization", nameof( externalId ) );
            }

            return $"{prefix}:{sanitizedId}";
        }

        /// <summary>
        /// Generates a deterministic card ID based on the rkey using base32-encoded SHA-256 hash.
        /// </summary>
        /// <param name="rkey">The record key to generate a card ID from.</param>
        /// <param name="maxLength">Maximum length of the card ID (default 32 characters).</param>
        /// <returns>A deterministic, URL-safe card ID string.</returns>
        /// <remarks>
        /// The card ID is generated as: base32(sha256(rkey)) truncated to maxLength.
        /// Base32 is used to ensure URL-safe, case-insensitive identifiers.
        /// </remarks>
        public static string GenerateCardId( string rkey, int maxLength = 32 ) {
            if (string.IsNullOrWhiteSpace( rkey )) {
                throw new ArgumentException( "Rkey cannot be null or empty", nameof( rkey ) );
            }

            if (maxLength <= 0) {
                throw new ArgumentException( "Max length must be positive", nameof( maxLength ) );
            }

            // Compute SHA-256 hash of the rkey
            byte[] hashBytes = SHA256.HashData( Encoding.UTF8.GetBytes( rkey ) );

            // Convert to base32 (using a standard base32 alphabet)
            string base32Hash = ToBase32( hashBytes );

            // Truncate to maxLength and return lowercase for consistency
            return base32Hash[..Math.Min( maxLength, base32Hash.Length )].ToLowerInvariant( );
        }

        /// <summary>
        /// Sanitizes an externalId to be URL-safe for use in an rkey.
        /// Removes all characters except alphanumerics and hyphens.
        /// </summary>
        private static string SanitizeForRkey( string externalId ) {
            if (string.IsNullOrWhiteSpace( externalId )) {
                return string.Empty;
            }

            // Keep only alphanumeric characters and hyphens
            StringBuilder sb = new( );
            foreach (char c in externalId) {
                if (char.IsLetterOrDigit( c ) || c == '-') {
                    _ = sb.Append( c );
                }
            }

            return sb.ToString( );
        }

        /// <summary>
        /// Converts a byte array to a base32 string using RFC 4648 alphabet.
        /// </summary>
        private static string ToBase32( byte[] input ) {
            if (input == null || input.Length == 0) {
                return string.Empty;
            }

            // Base32 alphabet (RFC 4648)
            const string base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            StringBuilder result = new( );
            int bits = 0;
            int value = 0;

            foreach (byte b in input) {
                value = (value << 8) | b;
                bits += 8;

                while (bits >= 5) {
                    bits -= 5;
                    int index = (value >> bits) & 0x1F;
                    _ = result.Append( base32Alphabet[index] );
                }
            }

            if (bits > 0) {
                int index = (value << (5 - bits)) & 0x1F;
                _ = result.Append( base32Alphabet[index] );
            }

            return result.ToString( );
        }
    }
}
