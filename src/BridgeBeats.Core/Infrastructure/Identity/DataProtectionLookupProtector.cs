using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity {

    /// <summary>
    /// Lookup protector for ASP.NET Core Identity personal data protection.
    /// Delegates encryption/decryption to the Data Protection system using key IDs as purpose strings.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="DataProtectionLookupProtector"/> class.
    /// </remarks>
    /// <param name="dataProtectionProvider">The Data Protection provider used for encryption.</param>
    public class DataProtectionLookupProtector( IDataProtectionProvider dataProtectionProvider ) : ILookupProtector {

        private readonly IDataProtectionProvider _dataProtectionProvider = dataProtectionProvider
            ?? throw new ArgumentNullException( nameof( dataProtectionProvider ) );

        /// <inheritdoc/>
        public string? Protect( string keyId, string? data ) {
            if (data is null) {
                return null;
            }

            IDataProtector protector = _dataProtectionProvider.CreateProtector( keyId );
            return protector.Protect( data );
        }

        /// <inheritdoc/>
        public string? Unprotect( string keyId, string? data ) {
            if (data is null) {
                return null;
            }

            IDataProtector protector = _dataProtectionProvider.CreateProtector( keyId );
            return protector.Unprotect( data );
        }
    }
}
