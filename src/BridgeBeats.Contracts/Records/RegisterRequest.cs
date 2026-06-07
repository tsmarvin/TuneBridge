using System.ComponentModel.DataAnnotations;

namespace BridgeBeats.Contracts.Records {

    /// <summary>Request for user registration.</summary>
    /// <param name="Email">User email address (will be used as unique identifier).</param>
    /// <param name="Password">User password.</param>
    public record RegisterRequest(
        [Required][EmailAddress] string Email,
        [Required] string Password
    );

}
