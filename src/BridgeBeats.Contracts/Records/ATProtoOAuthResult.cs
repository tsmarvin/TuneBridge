namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Result of a successful ATProto OAuth authorization.
/// </summary>
public record ATProtoOAuthResult {
    /// <summary>
    /// The user's ATProto DID.
    /// </summary>
    public required string Did { get; init; }

    /// <summary>
    /// The user's ATProto handle.
    /// </summary>
    public required string Handle { get; init; }

    /// <summary>
    /// The OAuth access token.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// The OAuth refresh token.
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// The DPoP private key in JWK format.
    /// </summary>
    public required string DPoPKeyJwk { get; init; }

    /// <summary>
    /// When the access token expires.
    /// </summary>
    public required DateTime TokenExpiration { get; init; }

    /// <summary>
    /// The scopes granted by the authorization.
    /// </summary>
    public required string Scope { get; init; }
}
