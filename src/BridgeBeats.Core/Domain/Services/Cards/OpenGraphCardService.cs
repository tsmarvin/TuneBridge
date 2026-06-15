using System.Collections.Concurrent;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Core.Infrastructure.Storage;

namespace BridgeBeats.Core.Domain.Services.Cards {

    /// <summary>
    /// In-memory store of resolved <see cref="MediaLinkResult"/>s addressable by a generated card
    /// id, used to back share/social-preview (OpenGraph) pages. Stored cards expire after a
    /// configured lifetime; expired entries are swept lazily during store operations rather than on
    /// a timer.
    /// </summary>
    /// <param name="domain">
    /// The public domain used to build card URLs. When null/empty the service is disabled
    /// (see <see cref="IsEnabled"/>).
    /// </param>
    /// <param name="expirationHours">How long, in hours, a stored card remains retrievable. Must be greater than zero.</param>
    /// <param name="cleanupInterval">
    /// How many store operations elapse between lazy expiry sweeps. Must be greater than zero.
    /// </param>
    /// <param name="maxEntries">
    /// The maximum number of entries retained in the store before nearest-expiry eviction begins. Must be greater than zero.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="expirationHours"/>, <paramref name="cleanupInterval"/>, or <paramref name="maxEntries"/> is not greater than zero.
    /// </exception>
    public class OpenGraphCardService( string domain, int expirationHours, int cleanupInterval, int maxEntries ) : IOpenGraphCardService {

        /// <summary>Gets a value indicating whether card generation is enabled, that is, whether a public domain is configured.</summary>
        public bool IsEnabled => string.IsNullOrWhiteSpace( domain ) == false;

        /// <summary>Gets the public domain used to build card URLs.</summary>
        public string Domain => domain;

        /// <summary>Thread-safe backing store mapping card id to the stored result and its expiry timestamp.</summary>
        private readonly ConcurrentDictionary<string, (MediaLinkResult Result, DateTime Expiry)> _store = new();
        /// <summary>The lifetime applied to each stored card, derived from the configured expiration hours.</summary>
        private readonly TimeSpan _expirationTime = expirationHours > 0
            ? TimeSpan.FromHours( expirationHours )
            : throw new ArgumentOutOfRangeException( nameof( expirationHours ), expirationHours, "Expiration hours must be greater than zero." );
        /// <summary>Running count of store operations, used to decide when to run a lazy expiry sweep.</summary>
        private int _operationCounter;
        /// <summary>The number of store operations between lazy expiry sweeps.</summary>
        private readonly int _cleanupInterval = cleanupInterval > 0
            ? cleanupInterval
            : throw new ArgumentOutOfRangeException( nameof( cleanupInterval ), cleanupInterval, "Cleanup interval must be greater than zero." );
        /// <summary>The maximum number of entries retained before nearest-expiry eviction kicks in.</summary>
        private readonly int _maxEntries = maxEntries > 0
            ? maxEntries
            : throw new ArgumentOutOfRangeException( nameof( maxEntries ), maxEntries, "Max entries must be greater than zero." );
        /// <summary>
        /// Guards the at-capacity evict-then-add critical section. The cap is soft: a concurrent
        /// distinct-id writer that passes the outer count check before eviction completes can cause the
        /// count to transiently exceed <c>maxEntries</c> by up to (concurrent writers − 1). The store
        /// is self-correcting — subsequent writes and lazy sweeps reclaim the overshoot — and cannot
        /// grow without bound.
        /// </summary>
        private readonly Lock _evictionLock = new();

        /// <summary>Returns the number of entries currently in the store. Exposed for testability.</summary>
        internal int Count => _store.Count;

        /// <summary>
        /// Stores a resolved result under a deterministically generated card id and returns the
        /// public card URL. If a non-expired entry already exists for the id its original expiry is
        /// preserved; otherwise the entry's expiry is reset. Triggers a lazy expiry sweep.
        /// </summary>
        /// <param name="result">The resolved multi-provider result to store.</param>
        /// <returns>The public <c>https://{domain}/card/{id}</c> URL for the stored card.</returns>
        public string StoreResult( MediaLinkResult result ) {
            CleanExpiredEntries( );

            // Generate deterministic ID based on rkey (always available with fallback)
            string rkey = RecordKeyGenerator.GenerateRkey( result );
            string id = RecordKeyGenerator.GenerateCardId( rkey );

            // When at capacity and this id is new, evict the nearest-expiry entry under a lock so the store stays bounded.
            if (_store.Count >= _maxEntries && !_store.ContainsKey( id )) {
                lock (_evictionLock) {
                    while (_store.Count >= _maxEntries && !_store.ContainsKey( id )) {
                        string? evictKey = _store
                            .OrderBy( kv => kv.Value.Expiry )
                            .Select( kv => kv.Key )
                            .FirstOrDefault();
                        if (evictKey is null) {
                            break;
                        }
                        _ = _store.TryRemove( evictKey, out _ );
                    }
                }
            }

            // Atomically add or update the entry, preserving expiry if not expired
            _ = _store.AddOrUpdate(
                id,
                (result, DateTime.UtcNow.Add( _expirationTime )),
                ( _, existing ) => existing.Expiry > DateTime.UtcNow
                    ? (result, existing.Expiry)
                    : (result, DateTime.UtcNow.Add( _expirationTime ))
            );

            // Generate the OpenGraph card URL
            return $"https://{domain.TrimEnd( '/' )}/card/{id}";
        }

        /// <summary>
        /// Retrieves the stored result for a card id, or <see langword="null"/> if no entry exists
        /// or the entry has expired. Expired entries are removed on access.
        /// </summary>
        /// <param name="id">The card id returned when the result was stored.</param>
        /// <returns>The stored result, or <see langword="null"/> when absent or expired.</returns>
        public MediaLinkResult? GetResult( string id ) {
            if (_store.TryGetValue( id, out (MediaLinkResult Result, DateTime Expiry) entry )) {
                if (entry.Expiry > DateTime.UtcNow) {
                    return entry.Result;
                }
                // Remove expired entry
                _ = _store.TryRemove( id, out _ );
            }
            return null;
        }

        /// <summary>
        /// Lazily sweeps expired entries from the store. Increments the operation counter and, only
        /// on every <see cref="_cleanupInterval"/>-th call, removes all entries whose expiry has
        /// passed. This amortizes cleanup across store operations instead of using a timer.
        /// </summary>
        private void CleanExpiredEntries( ) {
            // Clean every Nth operation for predictable memory management
            if (Interlocked.Increment( ref _operationCounter ) % _cleanupInterval == 0) {
                DateTime now = DateTime.UtcNow;
                List<string> expiredKeys = [.. _store
                    .Where( kv => kv.Value.Expiry <= now )
                    .Select( kv => kv.Key )];

                foreach (string? key in expiredKeys) {
                    _ = _store.TryRemove( key, out _ );
                }
            }
        }
    }
}
