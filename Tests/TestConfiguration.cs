using Microsoft.Extensions.Configuration;

namespace BridgeBeats.Tests;

/// <summary>
/// Loads configuration for tests that need real provider credentials, layering the web app's
/// <c>appsettings.json</c>, user secrets, and environment variables. Also exposes helpers that report
/// which provider credentials are present and builds the Aspire parameter arguments for app-host tests.
/// </summary>
public static class TestConfiguration {
    /// <summary>Guards lazy, thread-safe construction of the configuration root.</summary>
    private static readonly Lock s_lock = new( );

    /// <summary>
    /// The lazily built configuration root, shared across the test run.
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

    /// <summary>
    /// Builds the configuration root from the web app's <c>appsettings.json</c>, the web app's user
    /// secrets, and environment variables.
    /// </summary>
    private static IConfiguration LoadConfiguration( ) {
        return new ConfigurationBuilder( )
            .AddJsonFile( Path.Combine( "src", "BridgeBeats.Web", "appsettings.json" ), optional: true )
            .AddUserSecrets<BridgeBeats.Web.Program>( optional: true )
            .AddEnvironmentVariables( )
            .Build( );
    }

    /// <summary>
    /// Reads a configuration value by key, returning <c>null</c> when the value is missing or blank.
    /// </summary>
    /// <param name="key">The configuration key to read.</param>
    /// <returns>The value, or <c>null</c> if absent or whitespace.</returns>
    public static string? GetValue( string key ) {
        string? value = Configuration[key];
        return string.IsNullOrWhiteSpace( value ) ? null : value;
    }

    /// <summary>
    /// Reports whether both the Spotify client id and secret are configured.
    /// </summary>
    /// <returns><c>true</c> when Spotify credentials are present.</returns>
    public static bool HasSpotifyCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:SpotifyClientId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:SpotifyClientSecret" ) );

    /// <summary>
    /// Reports whether the Apple Music team id, key id, and key path are all configured.
    /// </summary>
    /// <returns><c>true</c> when Apple Music credentials are present.</returns>
    public static bool HasAppleMusicCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleTeamId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleKeyId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:AppleKeyPath" ) );

    /// <summary>
    /// Reports whether both the Tidal client id and secret are configured.
    /// </summary>
    /// <returns><c>true</c> when Tidal credentials are present.</returns>
    public static bool HasTidalCredentials( ) =>
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:TidalClientId" ) ) &&
        !string.IsNullOrWhiteSpace( GetValue( "BridgeBeats:TidalClientSecret" ) );

    /// <summary>
    /// Builds the command-line parameter arguments passed to the Aspire app host for distributed
    /// tests. Provider credentials are included only when present; the API key salt defaults to a test
    /// value, and the Discord and ATProto parameters are passed empty to disable those integrations.
    /// </summary>
    /// <returns>The array of <c>Parameters:*</c> argument strings for the app host.</returns>
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
