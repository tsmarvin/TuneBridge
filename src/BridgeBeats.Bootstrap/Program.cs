using System.Security.Cryptography;
using System.Text;

const string RedisPasswordFileName = "redis_password";

string outputDirectory = args.Length > 0 ? args[0] : "/app/bootstrap";
Directory.CreateDirectory( outputDirectory );

string? suppliedRedisPassword = Environment.GetEnvironmentVariable( "REDIS_PASSWORD" );
if (string.IsNullOrWhiteSpace( suppliedRedisPassword )) {
    suppliedRedisPassword = null;
} else if (suppliedRedisPassword.Length is < 32 or > 128 || !suppliedRedisPassword.All( IsSecretCharacter )) {
    Console.Error.WriteLine( "REDIS_PASSWORD must contain 32 to 128 base64 or base64url characters." );
    Environment.ExitCode = 1;
    return;
}

string redisPasswordPath = Path.Combine( outputDirectory, RedisPasswordFileName );
if (File.Exists( redisPasswordPath )) {
    if (suppliedRedisPassword is not null && !SecretsMatch( redisPasswordPath, suppliedRedisPassword )) {
        Console.Error.WriteLine( "REDIS_PASSWORD does not match the initialized Redis credential." );
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine( "The BridgeBeats Redis credential already exists; no changes were made." );
    return;
}

string redisPassword = suppliedRedisPassword ?? CreateSecret( 48 );
WriteNewFile(
    redisPasswordPath,
    redisPassword,
    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead
);

Console.WriteLine( "Created the BridgeBeats Redis credential." );

static string CreateSecret( int byteCount ) => Convert.ToBase64String( RandomNumberGenerator.GetBytes( byteCount ) )
    .TrimEnd( '=' )
    .Replace( '+', '-' )
    .Replace( '/', '_' );

static bool IsSecretCharacter( char value ) => char.IsAsciiLetterOrDigit( value ) || value is '+' or '/' or '=' or '-' or '_';

static bool SecretsMatch( string path, string suppliedSecret ) {
    byte[] stored = Encoding.UTF8.GetBytes( File.ReadAllText( path ).TrimEnd( '\r', '\n' ) );
    byte[] supplied = Encoding.UTF8.GetBytes( suppliedSecret );
    byte[] storedDigest = SHA256.HashData( stored );
    byte[] suppliedDigest = SHA256.HashData( supplied );
    return CryptographicOperations.FixedTimeEquals( storedDigest, suppliedDigest );
}

static void WriteNewFile( string path, string contents, UnixFileMode mode )
    => WriteNewBinaryFile( path, Encoding.UTF8.GetBytes( contents ), mode );

static void WriteNewBinaryFile( string path, byte[] contents, UnixFileMode mode ) {
    FileStreamOptions options = new( ) {
        Mode = FileMode.CreateNew,
        Access = FileAccess.Write,
        Share = FileShare.None
    };
    if (!OperatingSystem.IsWindows( )) {
        options.UnixCreateMode = mode;
    }

    using FileStream stream = new( path, options );
    stream.Write( contents );
}
