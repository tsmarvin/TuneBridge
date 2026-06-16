using BridgeBeats.Contracts.DTOs;

namespace BridgeBeats.Contracts.Records.WorkerApi;

/// <summary>
/// The single response envelope a provider worker returns from any of its <c>/lookup/*</c>
/// endpoints, wrapping the resolved <see cref="MusicLookupResult"/> with status information. The
/// endpoints reply with HTTP 200 even when the lookup fails, so <see cref="Success"/> and
/// <see cref="ErrorMessage"/> (not the HTTP status code) carry the outcome for the caller.
/// </summary>
/// <param name="Success">Whether the lookup succeeded.</param>
/// <param name="Result">The resolved item, or <see langword="null"/> when there is none.</param>
/// <param name="ErrorMessage">
/// A human-readable failure reason, or <see langword="null"/> when no error message is set.
/// </param>
/// <remarks>
/// Construct instances through the factory methods rather than the primary constructor. The three
/// factories do not all collapse to the same shape: <see cref="Ok(MusicLookupResult?)"/> sets
/// <see cref="Success"/> from whether its argument is non-null, so <c>Ok(null)</c> yields
/// <c>Success = false, Result = null</c>; <see cref="NotFound"/> yields
/// <c>Success = true, Result = null</c>; and <see cref="Error(string)"/> yields
/// <c>Success = false</c> with the supplied message. <c>Ok(null)</c> and <see cref="NotFound"/>
/// therefore differ in <see cref="Success"/> despite both carrying a <see langword="null"/> result.
/// </remarks>
public sealed record ProviderLookupResponse(
    bool Success,
    MusicLookupResult? Result,
    string? ErrorMessage
) {
    /// <summary>
    /// Creates a response from a lookup result. <see cref="Success"/> is set to <see langword="true"/>
    /// only when <paramref name="result"/> is non-null; a <see langword="null"/> argument produces a
    /// response with <c>Success = false</c> and no result.
    /// </summary>
    /// <param name="result">The resolved item, or <see langword="null"/> when nothing was found.</param>
    /// <returns>
    /// A response whose <see cref="Success"/> is <see langword="true"/> exactly when
    /// <paramref name="result"/> is non-null, with no error message.
    /// </returns>
    public static ProviderLookupResponse Ok( MusicLookupResult? result )
        => new( result is not null, result, null );

    /// <summary>
    /// Creates a failed response carrying an error reason.
    /// </summary>
    /// <param name="errorMessage">The human-readable reason the lookup failed.</param>
    /// <returns>
    /// A response with <see cref="Success"/> set to <see langword="false"/>, no result, and the
    /// supplied <paramref name="errorMessage"/>.
    /// </returns>
    public static ProviderLookupResponse Error( string errorMessage )
        => new( false, null, errorMessage );

    /// <summary>
    /// Creates a "nothing matched" response: the lookup ran without error but found no item.
    /// </summary>
    /// <returns>
    /// A response with <see cref="Success"/> set to <see langword="true"/>, a <see langword="null"/>
    /// result, and no error message.
    /// </returns>
    public static ProviderLookupResponse NotFound( )
        => new( true, null, null );
}
