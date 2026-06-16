using BridgeBeats.Contracts.Enums;

namespace BridgeBeats.Contracts.Interfaces {

    /// <summary>
    /// Resolves the per-provider request queue for a queueable request type, exposing either a
    /// single provider's queue or all registered queues.
    /// </summary>
    /// <typeparam name="T">The queueable request type the resolved queues carry. Must be a reference type implementing <see cref="IQueueableRequest"/>.</typeparam>
    /// <remarks>
    /// Implemented in <c>BridgeBeats.Core</c> by the internal nested <c>ProviderQueueResolver&lt;T&gt;</c>
    /// in <c>Infrastructure/Extensions/QueueServiceExtensions.cs</c>.
    /// </remarks>
    public interface IProviderQueueResolver<T> where T : class, IQueueableRequest {

        /// <summary>
        /// Returns the request queue registered for a given provider.
        /// </summary>
        /// <param name="provider">The provider whose queue is requested.</param>
        /// <returns>The <see cref="IRequestQueue{T}"/> registered for <paramref name="provider"/>.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when no queue is registered for the given provider.</exception>
        IRequestQueue<T> GetQueue( SupportedProviders provider );

        /// <summary>
        /// Returns every registered provider queue, keyed by provider.
        /// </summary>
        /// <returns>A read-only map from provider to its <see cref="IRequestQueue{T}"/>.</returns>
        IReadOnlyDictionary<SupportedProviders, IRequestQueue<T>> GetAllQueues( );
    }
}
