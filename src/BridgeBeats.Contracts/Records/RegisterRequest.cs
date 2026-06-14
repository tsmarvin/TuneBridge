using System.ComponentModel.DataAnnotations;

namespace BridgeBeats.Contracts.Records {

    /// <summary>
    /// First-party account registration request. Carries DataAnnotations validation on its
    /// fields so it can be bound and validated as a model.
    /// </summary>
    /// <param name="Email">The new account's email address, used as the unique identifier. Required and validated as an email address.</param>
    /// <param name="Password">The new account's password. Required.</param>
    public record RegisterRequest(
        [Required][EmailAddress] string Email,
        [Required] string Password
    );

}
