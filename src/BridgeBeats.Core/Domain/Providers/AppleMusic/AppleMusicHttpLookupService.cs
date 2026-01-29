using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic {

    /// <summary>
    /// HTTP-based lookup service for Apple Music that delegates to the Apple Music worker.
    /// </summary>
    public sealed class AppleMusicHttpLookupService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpMusicLookupService> logger
    ) : HttpMusicLookupService(
        SupportedProviders.AppleMusic,
        httpClientFactory,
        ProviderServiceExtensions.AppleMusicWorkerHttpClientName,
        logger
    );

}
