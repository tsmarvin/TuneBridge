using System.Collections.Concurrent;
using TuneBridge.Domain.Contracts.DTOs;
using TuneBridge.Domain.Implementations.Utilities;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {

    /// <summary>
    /// In-memory implementation of the OpenGraph card service for storing MediaLinkResult objects.
    /// </summary>
    public class OpenGraphCardService( string baseUrl ) : IOpenGraphCardService {

        /// <inheritdoc/>
        public bool IsEnabled => string.IsNullOrWhiteSpace( baseUrl ) == false;

        /// <inheritdoc/>
        public string BaseUrl => baseUrl;

        private readonly ConcurrentDictionary<string, (MediaLinkResult Result, DateTime Expiry)> _store = new();
        private readonly ConcurrentDictionary<string, (IReadOnlyList<MediaLinkResult> Results, DateTime Expiry)> _multiStore = new();
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

        /// <inheritdoc/>
        public string StoreMultipleResults( IEnumerable<MediaLinkResult> results ) {
            CleanExpiredEntries( );

            List<MediaLinkResult> resultList = results.ToList( );
            if (resultList.Count == 0) {
                throw new ArgumentException( "Results collection cannot be empty", nameof( results ) );
            }

            // Generate deterministic ID based on combined rkeys
            List<string> rkeys = resultList.Select( RecordKeyGenerator.GenerateRkey ).ToList( );
            string combinedRkeys = string.Join( "-", rkeys );
            string id = RecordKeyGenerator.GenerateCardId( combinedRkeys );

            // Store the collection
            _ = _multiStore.AddOrUpdate(
                id,
                (resultList.AsReadOnly( ), DateTime.UtcNow.Add( _expirationTime )),
                ( _, existing ) => existing.Expiry > DateTime.UtcNow
                    ? (resultList.AsReadOnly( ), existing.Expiry)
                    : (resultList.AsReadOnly( ), DateTime.UtcNow.Add( _expirationTime ))
            );

            // Generate the multi-card URL
            return $"https://{baseUrl.TrimEnd( '/' )}/card/multi/{id}";
        }

        /// <inheritdoc/>
        public IReadOnlyList<MediaLinkResult>? GetMultipleResults( string id ) {
            if (_multiStore.TryGetValue( id, out (IReadOnlyList<MediaLinkResult> Results, DateTime Expiry) entry )) {
                if (entry.Expiry > DateTime.UtcNow) {
                    return entry.Results;
                }
                // Remove expired entry
                _ = _multiStore.TryRemove( id, out _ );
            }
            return null;
        }

        private void CleanExpiredEntries( ) {
            // Clean every Nth operation for predictable memory management
            if (Interlocked.Increment( ref _operationCounter ) % CleanupInterval == 0) {
                DateTime now = DateTime.UtcNow;
                
                // Clean single result store
                List<string> expiredKeys = [.. _store
                    .Where( kv => kv.Value.Expiry <= now )
                    .Select( kv => kv.Key )];

                foreach (string? key in expiredKeys) {
                    _ = _store.TryRemove( key, out _ );
                }

                // Clean multi result store
                List<string> expiredMultiKeys = [.. _multiStore
                    .Where( kv => kv.Value.Expiry <= now )
                    .Select( kv => kv.Key )];

                foreach (string? key in expiredMultiKeys) {
                    _ = _multiStore.TryRemove( key, out _ );
                }
            }
        }
    }
}
