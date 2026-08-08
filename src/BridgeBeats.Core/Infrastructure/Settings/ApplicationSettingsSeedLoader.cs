using BridgeBeats.Contracts.Records;
using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Core.Infrastructure.Settings;

/// <summary>Maps first-run .NET configuration into the database settings update contract.</summary>
public static class ApplicationSettingsSeedLoader {
    /// <summary>
    /// Creates a first-write update from the <c>BridgeBeats</c> section. This method does not write
    /// the database and does not retain or display secret plaintext.
    /// </summary>
    /// <param name="configuration">Configuration populated by user-secrets, environment variables, or command line.</param>
    /// <returns>A complete initial settings update.</returns>
    public static ApplicationSettingsUpdate CreateUpdate( IConfiguration configuration ) {
        ArgumentNullException.ThrowIfNull( configuration );

        IConfigurationSection section = configuration.GetRequiredSection( "BridgeBeats" );
        ApplicationSettingsValues values = section.Get<ApplicationSettingsValues>( ) ?? new( );

        return new ApplicationSettingsUpdate {
            Values = values,
            Secrets = new ApplicationSettingsSecretUpdates {
                ApplePrivateKey = Replacement( section[nameof( ApplicationSettingsSecrets.ApplePrivateKey )] ),
                SpotifyClientSecret = Replacement( section[nameof( ApplicationSettingsSecrets.SpotifyClientSecret )] ),
                TidalClientSecret = Replacement( section[nameof( ApplicationSettingsSecrets.TidalClientSecret )] ),
                DiscordToken = Replacement( section[nameof( ApplicationSettingsSecrets.DiscordToken )] ),
                ATProtoPassword = Replacement( section[nameof( ApplicationSettingsSecrets.ATProtoPassword )] ),
                ATProtoOAuthSigningKey = Replacement(
                    section[nameof( ApplicationSettingsSecrets.ATProtoOAuthSigningKey )]
                ),
                ApiKeySalt = Replacement( section[nameof( ApplicationSettingsSecrets.ApiKeySalt )] ),
                InternalServiceKey = Replacement( section[nameof( ApplicationSettingsSecrets.InternalServiceKey )] )
            }
        };
    }

    private static ApplicationSecretUpdate Replacement( string? value ) =>
        string.IsNullOrWhiteSpace( value )
            ? ApplicationSecretUpdate.Keep( )
            : ApplicationSecretUpdate.Replace( value );
}
