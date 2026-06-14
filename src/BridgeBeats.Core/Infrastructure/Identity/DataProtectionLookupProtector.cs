using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity {

    /// <summary>
    /// Lookup protector for ASP.NET Core Identity personal data protection.
    /// </summary>
    /// <remarks>
    /// Delegates encryption and decryption to the ASP.NET Core Data Protection system, using the
    /// supplied key identifier as the protector's purpose string. Null input passes through
    /// unchanged. The underlying <see cref="IDataProtector"/> produces randomized ciphertext, so
    /// protected values cannot be matched by comparing ciphertext.
    /// </remarks>
    /// <param name="dataProtectionProvider">The Data Protection provider used to create per-key protectors.</param>
    public class DataProtectionLookupProtector( IDataProtectionProvider dataProtectionProvider ) : ILookupProtector {

        private readonly IDataProtectionProvider _dataProtectionProvider = dataProtectionProvider
            ?? throw new ArgumentNullException( nameof( dataProtectionProvider ) );

        /// <summary>
        /// Protects (encrypts) a value using a protector derived from the given key identifier.
        /// </summary>
        /// <param name="keyId">The key version identifier used to derive the protector.</param>
        /// <param name="data">The plaintext to protect, or <see langword="null"/>.</param>
        /// <returns>The protected ciphertext, or <see langword="null"/> when <paramref name="data"/> is <see langword="null"/>.</returns>
        public string? Protect( string keyId, string? data ) {
            if (data is null) {
                return null;
            }

            IDataProtector protector = _dataProtectionProvider.CreateProtector( keyId );
            return protector.Protect( data );
        }

        /// <summary>
        /// Unprotects (decrypts) a value using a protector derived from the given key identifier.
        /// </summary>
        /// <param name="keyId">The key version identifier used to derive the protector.</param>
        /// <param name="data">The ciphertext to unprotect, or <see langword="null"/>.</param>
        /// <returns>The recovered plaintext, or <see langword="null"/> when <paramref name="data"/> is <see langword="null"/>.</returns>
        public string? Unprotect( string keyId, string? data ) {
            if (data is null) {
                return null;
            }

            IDataProtector protector = _dataProtectionProvider.CreateProtector( keyId );
            return protector.Unprotect( data );
        }
    }
}
