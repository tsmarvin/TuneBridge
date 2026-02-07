using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity {

    /// <summary>
    /// Key ring for ASP.NET Core Identity personal data protection.
    /// Manages key IDs used as "purpose" strings by the Data Protection system.
    /// </summary>
    /// <remarks>
    /// The key ID is used as the Data Protection "purpose" string; actual cryptographic
    /// key management is handled by the Data Protection subsystem. Future key rotation
    /// adds new key IDs (e.g., "v2") and updates <see cref="CurrentKeyId"/>.
    /// </remarks>
    public class DataProtectionKeyRing : ILookupProtectorKeyRing {

        /// <summary>
        /// The current key ring version. Update this when rotating keys.
        /// </summary>
        private const string CurrentVersion = "v1";

        /// <summary>
        /// All known key IDs, ordered newest-first.
        /// When rotating, add the new version at the start and update <see cref="CurrentVersion"/>.
        /// Old versions must remain to decrypt existing data.
        /// </summary>
        private static readonly string[] s_allKeyIds = [CurrentVersion];

        /// <inheritdoc/>
        public string CurrentKeyId => CurrentVersion;

        /// <inheritdoc/>
        public IEnumerable<string> GetAllKeyIds( ) => s_allKeyIds;

        /// <inheritdoc/>
        public string this[string keyId] => keyId;
    }
}
