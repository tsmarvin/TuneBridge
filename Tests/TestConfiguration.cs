using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests;

/// <summary>
/// Provides configuration loading utilities for tests.
/// Loads configuration from appsettings.json and user secrets.
/// </summary>
public static class TestConfiguration {
    private static readonly Lock s_lock = new( );

    /// <summary>
    /// Gets the shared test configuration loaded from appsettings.json and user secrets.
    /// </summary>
    public static IConfiguration Configuration {
        get {
            if (field is null) {
                lock (s_lock) {
                    field ??= LoadConfiguration( );
                }
            }
            return field;
        }
    }

    private static IConfiguration LoadConfiguration( ) {
        return new ConfigurationBuilder( )
            .AddJsonFile( Path.Combine( "src", "BridgeBeats.Web", "appsettings.json" ), optional: true )
            .AddUserSecrets<BridgeBeats.Web.Program>( optional: true )
            .AddEnvironmentVariables( )
            .Build( );
    }

    /// <summary>
    /// Gets a configuration value, returning null if not found or empty.
    /// </summary>
    public static string? GetValue( string key ) {
        string? value = Configuration[key];
        return string.IsNullOrWhiteSpace( value ) ? null : value;
    }

    /// <summary>
    /// Checks if a provider has valid credentials configured.
    /// </summary>
    public static bool HasSpotifyCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:SpotifyClientId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:SpotifyClientSecret" ) );

    /// <summary>
    /// Checks if Apple Music has valid credentials configured.
    /// </summary>
    public static bool HasAppleMusicCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleTeamId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleKeyId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleKeyPath" ) );

    /// <summary>
    /// Checks if Tidal has valid credentials configured.
    /// </summary>
    public static bool HasTidalCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:TidalClientId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:TidalClientSecret" ) );

    /// <summary>
    /// Builds the Aspire parameter arguments array for passing config to DistributedApplicationTestingBuilder.
    /// </summary>
    public static string[] BuildAspireParameterArgs( ) {
        List<string> args = [ ];

        // Spotify
        if (HasSpotifyCredentials( )) {
            args.Add( $"Parameters:SpotifyClientId={GetValue( "BridgeBeats:SpotifyClientId" )}" );
            args.Add( $"Parameters:SpotifyClientSecret={GetValue( "BridgeBeats:SpotifyClientSecret" )}" );
        }

        // Apple Music
        if (HasAppleMusicCredentials( )) {
            args.Add( $"Parameters:AppleTeamId={GetValue( "BridgeBeats:AppleTeamId" )}" );
            args.Add( $"Parameters:AppleKeyId={GetValue( "BridgeBeats:AppleKeyId" )}" );
            args.Add( $"Parameters:AppleKeyPath={GetValue( "BridgeBeats:AppleKeyPath" )}" );
        }

        // Tidal
        if (HasTidalCredentials( )) {
            args.Add( $"Parameters:TidalClientId={GetValue( "BridgeBeats:TidalClientId" )}" );
            args.Add( $"Parameters:TidalClientSecret={GetValue( "BridgeBeats:TidalClientSecret" )}" );
        }

        // Security - always required for tests
        args.Add( $"Parameters:ApiKeySalt={GetValue( "BridgeBeats:ApiKeySalt" ) ?? "test_salt"}" );

        // Disable Discord in tests
        args.Add( "Parameters:DiscordToken=" );

        // Disable ATProto for direct mode tests (no queue)
        args.Add( "Parameters:ATProtoIdentifier=" );
        args.Add( "Parameters:ATProtoPassword=" );

        return [.. args];
    }
}
