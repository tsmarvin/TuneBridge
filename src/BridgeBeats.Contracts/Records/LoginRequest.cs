namespace BridgeBeats.Contracts.Records {

    /// <summary>Request for user login.</summary>
    /// <param name="Email">User email address.</param>
    /// <param name="Password">User password.</param>
    public record LoginRequest( string Email, string Password );

}
