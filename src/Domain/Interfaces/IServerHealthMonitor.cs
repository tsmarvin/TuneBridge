namespace TuneBridge.Domain.Interfaces {
    /// <summary>
    /// Monitors server health by tracking request outcomes and error rates.
    /// Used to determine if the server is overloaded and should pause processing.
    /// </summary>
    public interface IServerHealthMonitor {
        /// <summary>
        /// Records a successful request outcome.
        /// </summary>
        void RecordSuccess( );

        /// <summary>
        /// Records a failed request outcome with the HTTP status code.
        /// </summary>
        /// <param name="statusCode">The HTTP status code of the failed request.</param>
        void RecordFailure( int statusCode );

        /// <summary>
        /// Determines if the server is healthy based on recent error rates.
        /// </summary>
        /// <returns>True if the server is healthy and can accept more requests; otherwise, false.</returns>
        bool IsHealthy( );

        /// <summary>
        /// Gets the current error rate (0.0 to 1.0).
        /// </summary>
        double CurrentErrorRate { get; }

        /// <summary>
        /// Gets the total number of requests in the current time window.
        /// </summary>
        int TotalRequests { get; }

        /// <summary>
        /// Gets the total number of errors in the current time window.
        /// </summary>
        int TotalErrors { get; }
    }
}
