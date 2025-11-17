namespace TuneBridge.Domain.Contracts.Entities {

    /// <summary>
    /// Represents a cached MediaLinkResult entry stored in the SQLite database.
    /// This entity tracks the ATProto PDS record location and associated lookup entries.
    /// The actual MediaLinkResult data is always fetched from the PDS to ensure freshness.
    /// The Rkey serves as the primary key and is deterministic based on external IDs or metadata.
    /// </summary>
    public class MediaLinkCacheEntry {

        /// <summary>
        /// The deterministic record key (rkey) for the ATProto record.
        /// Primary format: "track:{externalId}" or "album:{externalId}" (e.g., "track:USRC12345678" or "album:123456789012").
        /// Fallback format: "metadata:{hash}" (e.g., "metadata:a1b2c3d4e5f6g7h8") is used when no externalId is available.
        /// This serves as the primary key for deterministic lookups.
        /// </summary>
        public string Rkey { get; set; } = string.Empty;

        /// <summary>
        /// The AT-URI of the record on ATProto PDS (e.g., at://did:plc:xxx/media.tunebridge.dev.lookup/yyy).
        /// This is deterministically generated based on the rkey.
        /// </summary>
        public string RecordUri { get; set; } = string.Empty;

        /// <summary>
        /// The timestamp when this record was created on the ATProto PDS.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// The timestamp when this record was last looked up (either created or refreshed on PDS).
        /// Used to determine if the record needs to be updated.
        /// </summary>
        public DateTime LastLookedUpAt { get; set; }

        /// <summary>
        /// Navigation property for related lookup entries (input links, service links, external IDs, metadata).
        /// </summary>
        public List<MediaLookupEntry> LookupEntries { get; set; } = [];
    }
}
