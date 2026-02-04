using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces {

    /// <summary>
    /// Resolves request queues by provider.
    /// </summary>
    /// <typeparam name="T">The type of request handled by the queues.</typeparam>
    public interface IProviderQueueResolver<T> where T : class, IQueueableRequest {
        /// <summary>
        /// Gets the request queue for a specific provider.
        /// </summary>
        /// <param name="provider">The provider.</param>
        /// <returns>The request queue for that provider.</returns>
        IRequestQueue<T> GetQueue( SupportedProviders provider );

        /// <summary>
        /// Gets all registered provider queues.
        /// </summary>
        /// <returns>A dictionary of providers to their queues.</returns>
        IReadOnlyDictionary<SupportedProviders, IRequestQueue<T>> GetAllQueues( );
    }
}
