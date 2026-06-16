namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// First-party account login request, authenticating by email and password. This is
    /// separate from the AT Protocol OAuth flow (see <see cref="AtProtoLoginRequest"/>).
    /// </summary>
    /// <param name="Email">The account's email address.</param>
    /// <param name="Password">The account's password.</param>
    public record LoginRequest( string Email, string Password );

}
