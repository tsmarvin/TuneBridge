using BridgeBeats.Contracts.Records;

namespace BridgeBeats.Core.Infrastructure.Settings;

/// <summary>Determines whether a stored settings snapshot can compose a usable runtime.</summary>
public static class ApplicationSettingsActivation {
    /// <summary>Reports whether Spotify has both its identifier and protected credential.</summary>
    public static bool IsSpotifyConfigured(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) => !string.IsNullOrWhiteSpace( values.SpotifyClientId ) &&
        secrets.SpotifyClientSecret.IsConfigured;

    /// <summary>Reports whether Apple Music has all signing-key inputs.</summary>
    public static bool IsAppleMusicConfigured(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) => !string.IsNullOrWhiteSpace( values.AppleTeamId ) &&
        !string.IsNullOrWhiteSpace( values.AppleKeyId ) &&
        secrets.ApplePrivateKey.IsConfigured;

    /// <summary>Reports whether Tidal has both its identifier and protected credential.</summary>
    public static bool IsTidalConfigured(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) => !string.IsNullOrWhiteSpace( values.TidalClientId ) &&
        secrets.TidalClientSecret.IsConfigured;

    /// <summary>Reports whether the ATProto service account has every required credential.</summary>
    public static bool IsAtProtoServiceAccountConfigured(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) => !string.IsNullOrWhiteSpace( values.ATProtoIdentifier ) &&
        !string.IsNullOrWhiteSpace( values.ATProtoUserDid ) &&
        secrets.ATProtoPassword.IsConfigured;

    /// <summary>Reports whether Discord can authenticate its calls to the Web process.</summary>
    public static bool CanStartDiscordWorker( ApplicationSettingsSecrets secrets ) {
        ArgumentNullException.ThrowIfNull( secrets );
        return secrets.DiscordToken.IsConfigured && secrets.InternalServiceKey.IsConfigured;
    }

    /// <summary>Reports whether queue-producing and queue-consuming worker topology is enabled.</summary>
    public static bool CanStartQueueTopology(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) {
        ArgumentNullException.ThrowIfNull( values );
        ArgumentNullException.ThrowIfNull( secrets );

        return values.Workers.UseWorkerServices && HasRunnableProvider( values, secrets );
    }

    /// <summary>Reports whether ATProto-dependent saga and maintenance workers may start.</summary>
    public static bool CanStartAtProtoWorkerTopology(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) => CanStartQueueTopology( values, secrets ) &&
        IsAtProtoServiceAccountConfigured( values, secrets );

    /// <summary>
    /// Reports whether the snapshot satisfies Web's startup invariants as well as providing at
    /// least one runnable music provider.
    /// </summary>
    public static bool CanStartWeb(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) {
        ArgumentNullException.ThrowIfNull( values );
        ArgumentNullException.ThrowIfNull( secrets );

        return secrets.ApiKeySalt.IsConfigured && HasRunnableProvider( values, secrets );
    }

    /// <summary>
    /// Reports whether at least one configured provider is available in the selected execution mode.
    /// </summary>
    public static bool HasRunnableProvider(
        ApplicationSettingsValues values,
        ApplicationSettingsSecrets secrets
    ) {
        ArgumentNullException.ThrowIfNull( values );
        ArgumentNullException.ThrowIfNull( secrets );

        bool spotifyConfigured = IsSpotifyConfigured( values, secrets );
        bool appleMusicConfigured = IsAppleMusicConfigured( values, secrets );
        bool tidalConfigured = IsTidalConfigured( values, secrets );

        return values.Workers.UseWorkerServices
            ? values.Workers.SpotifyWorkerEnabled && spotifyConfigured ||
              values.Workers.AppleMusicWorkerEnabled && appleMusicConfigured ||
              values.Workers.TidalWorkerEnabled && tidalConfigured
            : spotifyConfigured || appleMusicConfigured || tidalConfigured;
    }
}
