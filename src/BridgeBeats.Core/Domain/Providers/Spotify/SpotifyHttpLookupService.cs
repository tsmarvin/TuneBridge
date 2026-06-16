using BridgeBeats.Contracts.Enums;
using BridgeBeats.Core.Domain.Extensions;
using BridgeBeats.Core.Domain.Providers.Common;

namespace BridgeBeats.Core.Domain.Providers.Spotify {

    /// <summary>
    /// Spotify proxy that forwards lookups to the Spotify provider worker over HTTP, binding the
    /// configured <c>spotify-worker</c> named client.
    /// </summary>
    /// <remarks>
    /// This is the proxy variant of the Spotify lookup service: it calls a remote worker rather than the
    /// Spotify API directly. The in-worker variant that calls the real API is <see cref="SpotifyLookupService"/>.
    /// All behavior is inherited from <see cref="HttpMusicLookupService"/>; this type only supplies the
    /// provider and worker client name.
    /// </remarks>
    /// <param name="httpClientFactory">Factory used to create the worker HTTP client.</param>
    /// <param name="logger">Logger forwarded to the base proxy.</param>
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
