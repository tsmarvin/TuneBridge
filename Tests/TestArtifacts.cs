namespace BridgeBeats.Tests;

/// <summary>Creates discoverable, per-test filesystem artifacts beneath the repository TestResults tree.</summary>
internal static class TestArtifacts {
    private static readonly string s_root = Path.Combine( FindRepositoryRoot( ), "TestResults", "Artifacts" );

    /// <summary>Gets the shared test-artifact root.</summary>
    internal static string Root {
        get {
            _ = Directory.CreateDirectory( s_root );
            return s_root;
        }
    }

    /// <summary>Creates and returns a unique directory under the shared artifact root.</summary>
    internal static string CreateDirectory( string prefix ) {
        string path = Path.Combine( Root, $"{prefix}-{Guid.NewGuid( ):N}" );
        _ = Directory.CreateDirectory( path );
        return path;
    }

    /// <summary>Returns a unique file path under the shared artifact root.</summary>
    internal static string CreateFilePath( string prefix, string extension ) {
        string normalizedExtension = extension.StartsWith( ".", StringComparison.Ordinal )
            ? extension
            : $".{extension}";
        return Path.Combine( Root, $"{prefix}-{Guid.NewGuid( ):N}{normalizedExtension}" );
    }

    private static string FindRepositoryRoot( ) {
        DirectoryInfo? directory = new( AppContext.BaseDirectory );
        while (directory is not null) {
            if (File.Exists( Path.Combine( directory.FullName, "BridgeBeats.sln" ) )) {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException( "Could not locate the BridgeBeats repository root for test artifacts." );
    }
}
