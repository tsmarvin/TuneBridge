using System.Collections.Concurrent;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// In-memory implementation of IMediaCardService for storing MediaLinkResult instances
    /// for OpenGraph card generation.
    /// </summary>
    public class InMemoryMediaCardService : IMediaCardService {

        private readonly ConcurrentDictionary<string, MediaLinkResult> _storage = new( );
        private readonly TimeSpan _expirationTime = TimeSpan.FromHours( 24 );
        private readonly ConcurrentDictionary<string, DateTime> _expirationTimes = new( );

        /// <inheritdoc/>
        public string StoreResult( MediaLinkResult result ) {
            string id = Guid.NewGuid( ).ToString( "N" );
            _storage[id] = result;
            _expirationTimes[id] = DateTime.UtcNow.Add( _expirationTime );
            
            // Clean up expired entries
            CleanupExpiredEntries( );
            
            return id;
        }

        /// <inheritdoc/>
        public MediaLinkResult? GetResult( string id ) {
            if (_storage.TryGetValue( id, out MediaLinkResult? result )) {
                // Check if expired
                if (_expirationTimes.TryGetValue( id, out DateTime expirationTime ) && 
                    DateTime.UtcNow < expirationTime) {
                    return result;
                }
                // Remove expired entry
                _storage.TryRemove( id, out _ );
                _expirationTimes.TryRemove( id, out _ );
            }
            return null;
        }

        private void CleanupExpiredEntries( ) {
            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, DateTime> entry in _expirationTimes) {
                if (now >= entry.Value) {
                    _storage.TryRemove( entry.Key, out _ );
                    _expirationTimes.TryRemove( entry.Key, out _ );
                }
            }
        }
    }

}
