namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Result of a completed AT Protocol OAuth exchange: the live tokens and identity
/// produced by an authorization or refresh. This is the in-flight result of the OAuth
/// dance; the at-rest, serialized form is <see cref="ATProtoPersistedCredentials"/>.
/// </summary>
/// <remarks>
/// Returned by the OAuth service when completing an authorization or refreshing tokens.
/// Carries sensitive material; treat it as a credential-bearing value.
/// </remarks>
public sealed record ATProtoOAuthResult {

    /// <summary>The authenticated account's decentralized identifier (DID).</summary>
    public required string Did { get; init; }

    /// <summary>The authenticated account's Bluesky handle (for example, <c>alice.bsky.social</c>).</summary>
    public required string Handle { get; init; }

    /// <summary>The OAuth access token used to authorize requests against the user's PDS.</summary>
    public required string AccessToken { get; init; }

    /// <summary>The OAuth refresh token used to obtain a new access token once the current one expires.</summary>
    public required string RefreshToken { get; init; }

    /// <summary>The DPoP proof key, serialized as a JSON Web Key (JWK), used to bind tokens to the holder.</summary>
    public required string DPoPKeyJwk { get; init; }

    /// <summary>The absolute instant at which <see cref="AccessToken"/> expires.</summary>
    public required DateTime TokenExpiration { get; init; }

    /// <summary>The OAuth scope string granted by the authorization.</summary>
    public required string Scope { get; init; }
}
