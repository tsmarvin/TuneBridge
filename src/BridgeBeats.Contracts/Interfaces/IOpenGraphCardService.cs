using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Interfaces;

/// <summary>
/// Produces and serves OpenGraph cards for lookup results: temporarily stores a result under a
/// card id and retrieves it later when the card is requested.
/// </summary>
/// <remarks>
/// Implemented in <c>BridgeBeats.Core</c> by <c>OpenGraphCardService</c>
/// (<c>Domain/Services/Cards/OpenGraphCardService.cs</c>). Stored results expire after a
/// configured interval and are evicted on access or during periodic cleanup.
/// <see cref="IsEnabled"/> is the feature flag and <see cref="Domain"/> the public base domain
/// used to build card links.
/// </remarks>
public interface IOpenGraphCardService {

    /// <summary>
    /// Whether the OpenGraph card feature is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The public base domain used to build card links (for example <c>bridgebeats.link</c>).
    /// </summary>
    string Domain { get; }

    /// <summary>
    /// Stores a result for card rendering and returns the link to the stored card.
    /// </summary>
    /// <param name="result">The <see cref="MediaLinkResult"/> to back the card.</param>
    /// <returns>A link to the stored card, embedding the card id that <see cref="GetResult"/> reads back.</returns>
    string StoreResult( MediaLinkResult result );

    /// <summary>
    /// Retrieves a previously stored result by its card id.
    /// </summary>
    /// <param name="id">The card id embedded in the link returned by <see cref="StoreResult"/>.</param>
    /// <returns>
    /// The stored <see cref="MediaLinkResult"/>, or <see langword="null"/> when no result is
    /// stored under the given id or the entry has expired.
    /// </returns>
    MediaLinkResult? GetResult( string id );
}
