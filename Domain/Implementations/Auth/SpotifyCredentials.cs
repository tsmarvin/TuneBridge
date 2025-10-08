using System.Text;

namespace TuneBridge.Domain.Implementations.Auth {

    public sealed class SpotifyCredentials(
        string clientId,
        string clientSecret
    ) {
        public string Credentials { get; }
            = Convert.ToBase64String( Encoding.UTF8.GetBytes( $"{clientId}:{clientSecret}" ) );
    }

}
