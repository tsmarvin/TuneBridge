using System.Collections.Concurrent;
using TuneBridge.Common.Contracts.DTOs;
using TuneBridge.Common.Contracts.Interfaces;
using TuneBridge.Common.Utilities;

namespace TuneBridge.Web.Services {

    /// <summary>
    /// In-memory implementation of the OpenGraph card service for storing MediaLinkResult objects.
    /// </summary>
    public class OpenGraphCardService( string baseUrl ) : IOpenGraphCardService {

        /// <inheritdoc/>
        public bool IsEnabled => string.IsNullOrWhiteSpace( baseUrl ) == false;

        /// <inheritdoc/>
        public string BaseUrl => baseUrl;

        private readonly ConcurrentDictionary<string, (MediaLinkResult Result, DateTime Expiry)> _store = new();
        private readonly TimeSpan _expirationTime = TimeSpan.FromDays( 6 );
        private int _operationCounter;
        private const int CleanupInterval = 10000;

        /// <inheritdoc/>
        public string StoreResult( MediaLinkResult result ) {
            CleanExpiredEntries( );

            // Generate deterministic ID based on rkey (always available with fallback)
            string rkey = RecordKeyGenerator.GenerateRkey( result );
            string id = RecordKeyGenerator.GenerateCardId( rkey );

            // Atomically add or update the entry, preserving expiry if not expired
            _ = _store.AddOrUpdate(
                id,
                (result, DateTime.UtcNow.Add( _expirationTime )),
                ( _, existing ) => existing.Expiry > DateTime.UtcNow
                    ? (result, existing.Expiry)
                    : (result, DateTime.UtcNow.Add( _expirationTime ))
            );

            // Generate the OpenGraph card URL
            return $"https://{baseUrl.TrimEnd( '/' )}/card/{id}";
        }

        /// <inheritdoc/>
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

        private void CleanExpiredEntries( ) {
            // Clean every Nth operation for predictable memory management
            if (Interlocked.Increment( ref _operationCounter ) % CleanupInterval == 0) {
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
