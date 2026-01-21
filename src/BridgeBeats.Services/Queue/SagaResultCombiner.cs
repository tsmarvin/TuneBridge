using System.Text.Json;
using BridgeBeats.Contracts.DTOs;
using BridgeBeats.Contracts.Enums;
using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Logging;

namespace BridgeBeats.Services.Queue;

/// <summary>
/// Combines results from multiple providers into a unified <see cref="MediaLinkResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// When a saga completes (all providers have finished their lookups), this service
/// assembles the final <see cref="MediaLinkResult"/> by merging successful results
/// from each provider.
/// </para>
/// <para>
/// The combiner takes the best available metadata from successful lookups and
/// creates a unified result that includes links to all providers where the
/// track/album was found.
/// </para>
/// </remarks>
public sealed class SagaResultCombiner {
    private readonly ILogger<SagaResultCombiner> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaResultCombiner"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    public SagaResultCombiner( ILogger<SagaResultCombiner> logger ) {
        _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
        _jsonOptions = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Combines results from all successful provider lookups in a saga into a single <see cref="MediaLinkResult"/>.
    /// </summary>
    /// <param name="saga">The completed saga containing provider results.</param>
    /// <param name="allowIncomplete">
    /// If <c>true</c>, allows combining results even if the saga is incomplete (for partial results).
    /// Default is <c>false</c>.
    /// </param>
    /// <returns>
    /// A <see cref="MediaLinkResult"/> combining all successful results, or <c>null</c> if no providers succeeded.
    /// </returns>
    public MediaLinkResult? CombineResults( LookupSagaState saga, bool allowIncomplete = false ) {
        ArgumentNullException.ThrowIfNull( saga );

        if (!saga.IsComplete && !allowIncomplete) {
            _logger.LogWarning(
                "Attempted to combine results for incomplete saga {SagaId}",
                saga.SagaId
            );
        }

        // Extract successful results from the saga
        List<(SupportedProviders provider, MusicLookupResult result)> successfulResults = [];

        foreach ((SupportedProviders provider, ProviderLookupState state) in saga.ProviderStates) {
            if (!state.IsSuccess || string.IsNullOrEmpty( state.ResultJson )) {
                continue;
            }

            try {
                MusicLookupResult? result = JsonSerializer.Deserialize<MusicLookupResult>(
                    state.ResultJson,
                    _jsonOptions
                );

                if (result is not null) {
                    successfulResults.Add( (provider, result) );
                }
            } catch (JsonException ex) {
                _logger.LogWarning(
                    ex,
                    "Failed to deserialize result for provider {Provider} in saga {SagaId}",
                    provider,
                    saga.SagaId
                );
            }
        }

        if (successfulResults.Count == 0) {
            _logger.LogWarning(
                "Saga {SagaId} has no successful results yet",
                saga.SagaId
            );
            return null;
        }

        return BuildMediaLinkResult( successfulResults, saga );
    }

    /// <summary>
    /// Combines results from pre-parsed <see cref="MusicLookupResult"/> objects.
    /// </summary>
    /// <param name="results">The collection of provider results to combine.</param>
    /// <param name="lookupValue">The original lookup value used for the saga.</param>
    /// <returns>
    /// A <see cref="MediaLinkResult"/> combining all results, or <c>null</c> if no results provided.
    /// </returns>
    public MediaLinkResult? CombineResults(
        IEnumerable<(SupportedProviders provider, MusicLookupResult result)> results,
        string lookupValue
    ) {
        ArgumentNullException.ThrowIfNull( results );

        List<(SupportedProviders provider, MusicLookupResult result)> resultList = results.ToList( );

        if (resultList.Count == 0) {
            return null;
        }

        // Create a minimal saga state for the build method
        LookupSagaState saga = new( ) {
            SagaId = string.Empty,
            LookupKey = lookupValue,
            LookupType = LookupRequestType.IsrcLookup,
            LookupValue = lookupValue
        };

        return BuildMediaLinkResult( resultList, saga );
    }

    private MediaLinkResult BuildMediaLinkResult(
        List<(SupportedProviders provider, MusicLookupResult result)> results,
        LookupSagaState saga
    ) {
        MediaLinkResult mediaLinkResult = new( );

        // Mark the first result as primary
        bool isFirst = true;
        foreach ((SupportedProviders provider, MusicLookupResult result) in results) {
            result.IsPrimary = isFirst;
            isFirst = false;

            mediaLinkResult.Results[provider] = result;
        }

        _logger.LogDebug(
            "Combined {Count} provider results for saga {SagaId}",
            results.Count,
            saga.SagaId
        );

        return mediaLinkResult;
    }
}
