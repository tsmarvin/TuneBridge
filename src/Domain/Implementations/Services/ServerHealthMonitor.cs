using System.Collections.Concurrent;
using TuneBridge.Domain.Interfaces;

namespace TuneBridge.Domain.Implementations.Services {
    /// <summary>
    /// Monitors server health by tracking request outcomes within a sliding time window.
    /// Thread-safe implementation suitable for concurrent access.
    /// </summary>
    public class ServerHealthMonitor : IServerHealthMonitor {
        private readonly double _maxErrorRate;
        private readonly TimeSpan _timeWindow;
        private readonly int _minRequestsForErrorRate;
        private readonly ConcurrentQueue<RequestOutcome> _outcomes = new( );
        private readonly object _lock = new( );

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerHealthMonitor"/> class.
        /// </summary>
        /// <param name="maxErrorRate">Maximum allowed error rate (0.0 to 1.0) before server is considered unhealthy.</param>
        /// <param name="timeWindowMinutes">Time window in minutes for calculating error rate.</param>
        /// <param name="minRequestsForErrorRate">Minimum number of requests before error rate calculation applies.</param>
        public ServerHealthMonitor( double maxErrorRate, int timeWindowMinutes, int minRequestsForErrorRate ) {
            _maxErrorRate = Math.Clamp( maxErrorRate, 0.0, 1.0 );
            _timeWindow = TimeSpan.FromMinutes( timeWindowMinutes );
            _minRequestsForErrorRate = Math.Max( 1, minRequestsForErrorRate );
        }

        /// <inheritdoc/>
        public void RecordSuccess( ) {
            _outcomes.Enqueue( new RequestOutcome( DateTime.UtcNow, true, 0 ) );
            CleanupOldEntries( );
        }

        /// <inheritdoc/>
        public void RecordFailure( int statusCode ) {
            _outcomes.Enqueue( new RequestOutcome( DateTime.UtcNow, false, statusCode ) );
            CleanupOldEntries( );
        }

        /// <inheritdoc/>
        public bool IsHealthy( ) {
            CleanupOldEntries( );
            return CurrentErrorRate <= _maxErrorRate;
        }

        /// <inheritdoc/>
        public double CurrentErrorRate {
            get {
                CleanupOldEntries( );
                lock (_lock) {
                    RequestOutcome[] recentOutcomes = _outcomes.ToArray( );
                    if (recentOutcomes.Length < _minRequestsForErrorRate) {
                        // Not enough data yet, assume healthy
                        return 0.0;
                    }

                    int errorCount = recentOutcomes.Count( o => !o.IsSuccess );
                    return (double)errorCount / recentOutcomes.Length;
                }
            }
        }

        /// <inheritdoc/>
        public int TotalRequests {
            get {
                CleanupOldEntries( );
                return _outcomes.Count;
            }
        }

        /// <inheritdoc/>
        public int TotalErrors {
            get {
                CleanupOldEntries( );
                return _outcomes.Count( o => !o.IsSuccess );
            }
        }

        /// <summary>
        /// Removes entries older than the time window.
        /// </summary>
        private void CleanupOldEntries( ) {
            DateTime cutoff = DateTime.UtcNow - _timeWindow;
            lock (_lock) {
                while (_outcomes.TryPeek( out RequestOutcome? oldest ) && oldest.Timestamp < cutoff) {
                    _ = _outcomes.TryDequeue( out _ );
                }
            }
        }

        /// <summary>
        /// Represents the outcome of a single request.
        /// </summary>
        private record RequestOutcome( DateTime Timestamp, bool IsSuccess, int StatusCode );
    }
}
