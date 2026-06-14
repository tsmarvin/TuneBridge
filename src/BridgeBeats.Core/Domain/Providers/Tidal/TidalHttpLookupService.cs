using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Domain.Providers.Tidal {

    /// <summary>
    /// Thin HTTP proxy that forwards Tidal lookups to the remote Tidal worker service
    /// rather than calling the Tidal API directly.
    /// </summary>
    /// <remarks>
    /// This is the worker-delegating half of the provider abstraction. It extends
    /// <see cref="HttpMusicLookupService"/> and supplies only the Tidal-specific
    /// binding: the <see cref="SupportedProviders.Tidal"/> provider and the named HTTP
    /// client identified by
    /// <see cref="ProviderServiceExtensions.TidalWorkerHttpClientName"/>. All lookup
    /// behaviour is inherited from the base proxy, which posts to the worker's
    /// <c>/lookup/*</c> endpoints. The direct Tidal API client is
    /// <see cref="TidalLookupService"/>.
    /// </remarks>
    /// <param name="httpClientFactory">
    /// Factory used to create the named <c>tidal-worker</c> HTTP client.
    /// </param>
    /// <param name="logger">Logger for the base HTTP lookup proxy.</param>
    public sealed class TidalHttpLookupService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpMusicLookupService> logger
    ) : HttpMusicLookupService(
        SupportedProviders.Tidal,
        httpClientFactory,
        ProviderServiceExtensions.TidalWorkerHttpClientName,
        logger
    );

}
