using System.Collections.Concurrent;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// In-memory implementation of the OpenGraph card service for storing MediaLinkResult objects.
    /// </summary>
    public class OpenGraphCardService : IOpenGraphCardService {

        private readonly ConcurrentDictionary<string, (MediaLinkResult Result, DateTime Expiry)> _store = new();
        private readonly TimeSpan _expirationTime = TimeSpan.FromHours( 24 );
        private int _operationCounter;
        private const int CleanupInterval = 100;

        /// <inheritdoc/>
        public string StoreResult( MediaLinkResult result ) {
            CleanExpiredEntries();
            
            string id = Guid.NewGuid().ToString( "N" );
            DateTime expiry = DateTime.UtcNow.Add( _expirationTime );
            _store[id] = (result, expiry);
            
            return id;
        }

        /// <inheritdoc/>
        public MediaLinkResult? GetResult( string id ) {
            if (_store.TryGetValue( id, out var entry )) {
                if (entry.Expiry > DateTime.UtcNow) {
                    return entry.Result;
                }
                // Remove expired entry
                _store.TryRemove( id, out _ );
            }
            return null;
        }

        private void CleanExpiredEntries() {
            // Clean every Nth operation for predictable memory management
            if (Interlocked.Increment( ref _operationCounter ) % CleanupInterval == 0) {
                var now = DateTime.UtcNow;
                var expiredKeys = _store
                    .Where( kv => kv.Value.Expiry <= now )
                    .Select( kv => kv.Key )
                    .ToList();
                
                foreach (var key in expiredKeys) {
                    _store.TryRemove( key, out _ );
                }
            }
        }
    }
}
