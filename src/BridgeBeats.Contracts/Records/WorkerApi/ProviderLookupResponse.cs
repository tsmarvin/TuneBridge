using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// Response from a provider worker lookup operation.
/// Wraps the <see cref="MusicLookupResult"/> with additional status information.
/// </summary>
/// <param name="Success">Whether the lookup succeeded.</param>
/// <param name="Result">The lookup result if successful, null otherwise.</param>
/// <param name="ErrorMessage">Error message if the lookup failed, null otherwise.</param>
public sealed record ProviderLookupResponse(
    bool Success,
    MusicLookupResult? Result,
    string? ErrorMessage
) {
    /// <summary>
    /// Creates a successful response with the given result.
    /// </summary>
    /// <param name="result">The lookup result.</param>
    /// <returns>A successful response.</returns>
    public static ProviderLookupResponse Ok( MusicLookupResult? result )
        => new( result is not null, result, null );

    /// <summary>
    /// Creates a failure response with the given error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A failure response.</returns>
    public static ProviderLookupResponse Error( string errorMessage )
        => new( false, null, errorMessage );

    /// <summary>
    /// Creates a not-found response.
    /// </summary>
    /// <returns>A not-found response.</returns>
    public static ProviderLookupResponse NotFound( )
        => new( true, null, null );
}
