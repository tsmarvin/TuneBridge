using Microsoft.AspNetCore.Identity;

namespace BridgeBeats.Core.Infrastructure.Identity {

    /// <summary>
    /// Key ring for ASP.NET Core Identity personal data protection, naming the active key version.
    /// </summary>
    /// <remarks>
    /// The key identifier is used as the Data Protection "purpose" string; actual cryptographic key
    /// management is handled by the Data Protection subsystem, so this type names key versions only
    /// and holds no key material. A single version, <c>"v1"</c>, is currently exposed. Rotating keys
    /// means adding a new version at the start of the known set and updating <see cref="CurrentKeyId"/>;
    /// old versions must remain so existing data can still be decrypted.
    /// </remarks>
    public class DataProtectionKeyRing : ILookupProtectorKeyRing {

        /// <summary>
        /// The current key ring version. Update this when rotating keys.
        /// </summary>
        private const string CurrentVersion = "v1";

        /// <summary>
        /// All known key IDs, ordered newest-first. When rotating, add the new version at the start
        /// and update <see cref="CurrentVersion"/>. Old versions must remain to decrypt existing data.
        /// </summary>
        private static readonly string[] s_allKeyIds = [CurrentVersion];

        /// <summary>Gets the identifier of the key version to use for new protection operations.</summary>
        public string CurrentKeyId => CurrentVersion;

        /// <summary>Gets all key version identifiers known to this key ring.</summary>
        /// <returns>A sequence containing every known key version identifier.</returns>
        public IEnumerable<string> GetAllKeyIds( ) => s_allKeyIds;

        /// <summary>Gets the key associated with the supplied key identifier.</summary>
        /// <param name="keyId">The key version identifier to resolve.</param>
        /// <returns>The supplied <paramref name="keyId"/> unchanged.</returns>
        public string this[string keyId] => keyId;
    }
}
