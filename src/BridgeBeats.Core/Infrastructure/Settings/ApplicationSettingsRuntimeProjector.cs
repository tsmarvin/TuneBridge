using System.Globalization;
using System.Text.Json;
using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Core.Infrastructure.Settings;

/// <summary>Projects one authoritative database snapshot into a Web child-process configuration.</summary>
public static class ApplicationSettingsRuntimeProjector {
    /// <summary>Creates the flat configuration contract consumed by BridgeBeats.Web.</summary>
    /// <param name="snapshot">The authoritative persisted revision.</param>
    /// <param name="identityConnectionString">The bootstrap database connection string.</param>
    /// <param name="dataProtectionKeyPath">The bootstrap Data Protection key-ring path.</param>
    /// <param name="externalDomain">The deployment-controlled public domain.</param>
    /// <returns>A complete Web child-process configuration snapshot.</returns>
    public static IReadOnlyDictionary<string, string?> ProjectWeb(
        ApplicationSettingsSnapshot snapshot,
        string identityConnectionString,
        string dataProtectionKeyPath,
        string externalDomain
    ) {
        ArgumentNullException.ThrowIfNull( snapshot );
        ArgumentException.ThrowIfNullOrWhiteSpace( identityConnectionString );
        ArgumentException.ThrowIfNullOrWhiteSpace( dataProtectionKeyPath );
        ArgumentException.ThrowIfNullOrWhiteSpace( externalDomain );

        ApplicationSettingsValues values = snapshot.Values;
        ApplicationSettingsSecrets secrets = snapshot.Secrets;

        return new Dictionary<string, string?> {
            ["BridgeBeats:SetupRequired"] = "false",
            ["BridgeBeats:SettingsRevision"] = snapshot.Revision,
            ["BridgeBeats:IdentityConnectionString"] = identityConnectionString,
            ["BridgeBeats:DataProtectionKeyPath"] = dataProtectionKeyPath,
            ["BridgeBeats:AppleTeamId"] = values.AppleTeamId,
            ["BridgeBeats:AppleKeyId"] = values.AppleKeyId,
            ["BridgeBeats:ApplePrivateKey"] = secrets.ApplePrivateKey.Reveal( ),
            ["BridgeBeats:SpotifyClientId"] = values.SpotifyClientId,
            ["BridgeBeats:SpotifyClientSecret"] = secrets.SpotifyClientSecret.Reveal( ),
            ["BridgeBeats:TidalClientId"] = values.TidalClientId,
            ["BridgeBeats:TidalClientSecret"] = secrets.TidalClientSecret.Reveal( ),
            ["BridgeBeats:DiscordToken"] = secrets.DiscordToken.Reveal( ),
            ["BridgeBeats:ATProtoIdentifier"] = values.ATProtoIdentifier,
            ["BridgeBeats:ATProtoPassword"] = secrets.ATProtoPassword.Reveal( ),
            ["BridgeBeats:ATProtoUserDID"] = values.ATProtoUserDid,
            ["BridgeBeats:ATProtoPdsUri"] = values.ATProtoPdsUri,
            ["BridgeBeats:ATProtoOAuthSigningKey"] = secrets.ATProtoOAuthSigningKey.Reveal( ),
            ["BridgeBeats:ApiKeySalt"] = secrets.ApiKeySalt.Reveal( ),
            ["BridgeBeats:InternalServiceKey"] = secrets.InternalServiceKey.Reveal( ),
            ["BridgeBeats:Domain"] = externalDomain,
            ["BridgeBeats:RateLimitRequestsPerHour"] = Invariant( values.RateLimitRequestsPerHour ),
            ["BridgeBeats:CacheDays"] = Invariant( values.CacheDays ),
            ["BridgeBeats:ATProtoSessionTtlDays"] = Invariant( values.ATProtoSessionTtlDays ),
            ["BridgeBeats:CardCacheExpirationHours"] = Invariant( values.CardCacheExpirationHours ),
            ["BridgeBeats:CardCacheCleanupInterval"] = Invariant( values.CardCacheCleanupInterval ),
            ["BridgeBeats:CardCacheMaxEntries"] = Invariant( values.CardCacheMaxEntries ),
            ["BridgeBeats:Workers:UseWorkerServices"] = Boolean( values.Workers.UseWorkerServices ),
            ["BridgeBeats:Workers:SpotifyWorkerEnabled"] = Boolean( values.Workers.SpotifyWorkerEnabled ),
            ["BridgeBeats:Workers:AppleMusicWorkerEnabled"] = Boolean( values.Workers.AppleMusicWorkerEnabled ),
            ["BridgeBeats:Workers:TidalWorkerEnabled"] = Boolean( values.Workers.TidalWorkerEnabled ),
            ["BridgeBeats:Resilience:MaxRetryAfterSeconds"] = Invariant( values.Resilience.MaxRetryAfterSeconds ),
            ["BridgeBeats:Resilience:MaxRetryAttempts"] = Invariant( values.Resilience.MaxRetryAttempts ),
            ["BridgeBeats:Resilience:TotalTimeoutMinutes"] = Invariant( values.Resilience.TotalTimeoutMinutes ),
            ["BridgeBeats:Resilience:AttemptTimeoutSeconds"] = Invariant( values.Resilience.AttemptTimeoutSeconds ),
            ["BridgeBeats:QueueSnapshot"] = JsonSerializer.Serialize( values.Queue ),
            ["BridgeBeats:SpotifyBatchSnapshot"] = JsonSerializer.Serialize( values.Spotify.Batch )
        };
    }

    private static string Invariant( int value ) => value.ToString( CultureInfo.InvariantCulture );

    private static string Boolean( bool value ) => value ? "true" : "false";
}
