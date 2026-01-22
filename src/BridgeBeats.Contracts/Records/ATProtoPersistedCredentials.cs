using System.Text.Json.Serialization;

namespace BridgeBeats.Contracts.Records;

/// <summary>
/// Represents persisted ATProto session credentials for storage in Redis.
/// Contains the minimum data needed to restore a session without re-authenticating.
/// </summary>
/// <remarks>
/// Per idunno.Bluesky documentation, access tokens are short-lived and should NOT be persisted.
/// Only the refresh token and related metadata are stored for session restoration.
/// See: https://bluesky.idunno.dev/docs/savingAndRestoringAuthentication.html
/// </remarks>
public sealed record ATProtoPersistedCredentials {

    /// <summary>
    /// The refresh token for obtaining new access tokens.
    /// </summary>
    [JsonPropertyName( "refreshToken" )]
    public required string RefreshToken { get; init; }

    /// <summary>
    /// The DPoP proof key serialized as Base64, if DPoP authentication is used.
    /// May be null for legacy authentication.
    /// </summary>
    [JsonPropertyName( "dPoPProofKey" )]
    public string? DPoPProofKey { get; init; }

    /// <summary>
    /// The DPoP nonce value from the server, if DPoP authentication is used.
    /// May be null for legacy authentication.
    /// </summary>
    [JsonPropertyName( "dPoPNonce" )]
    public string? DPoPNonce { get; init; }

    /// <summary>
    /// The service URI that issued the tokens (e.g., the PDS endpoint).
    /// </summary>
    [JsonPropertyName( "service" )]
    public required string Service { get; init; }

    /// <summary>
    /// The DID (Decentralized Identifier) of the authenticated account.
    /// </summary>
    [JsonPropertyName( "did" )]
    public required string Did { get; init; }

    /// <summary>
    /// The handle of the authenticated account.
    /// Useful for logging and debugging.
    /// </summary>
    [JsonPropertyName( "handle" )]
    public required string Handle { get; init; }

    /// <summary>
    /// The authentication type (e.g., "Bearer" or "DPoP").
    /// </summary>
    [JsonPropertyName( "authenticationType" )]
    public required string AuthenticationType { get; init; }

    /// <summary>
    /// Timestamp when these credentials were persisted.
    /// Used for monitoring and debugging credential age.
    /// </summary>
    [JsonPropertyName( "persistedAt" )]
    public required DateTimeOffset PersistedAt { get; init; }

}
