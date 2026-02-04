using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Domain.Providers.Spotify {

    /// <summary>
    /// HTTP-based lookup service for Spotify that delegates to the Spotify worker.
    /// </summary>
    public sealed class SpotifyHttpLookupService(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpMusicLookupService> logger
    ) : HttpMusicLookupService(
        SupportedProviders.Spotify,
        httpClientFactory,
        ProviderServiceExtensions.SpotifyWorkerHttpClientName,
        logger
    );

}
