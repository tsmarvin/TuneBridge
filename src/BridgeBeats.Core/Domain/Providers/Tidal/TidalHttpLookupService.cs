using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Domain.Providers.Tidal {

    /// <summary>
    /// HTTP-based lookup service for Tidal that delegates to the Tidal worker.
    /// </summary>
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
