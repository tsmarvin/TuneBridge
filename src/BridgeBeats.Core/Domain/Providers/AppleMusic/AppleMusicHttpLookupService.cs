using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Domain.Providers.AppleMusic {

    /// <summary>
    /// HTTP proxy that forwards Apple Music lookups to the remote Apple Music worker service rather than
    /// calling the Apple Music API directly.
    /// </summary>
    /// <remarks>
    /// This is the distributed-path counterpart to <see cref="AppleMusicLookupService"/>. It inherits all
    /// behavior from <see cref="HttpMusicLookupService"/>, binding the named worker HTTP client and tagging
    /// requests as the Apple Music provider. The direct API client is
    /// <see cref="AppleMusicLookupService"/>.
    /// </remarks>
    /// <param name="httpClientFactory">Factory used to obtain the named worker HTTP client.</param>
    /// <param name="logger">Logger for the base proxy behavior.</param>
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
