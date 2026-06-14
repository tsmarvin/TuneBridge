using System.Security.Claims;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Resolves a presented API key to an authenticated user for the
/// <c>AspNetCore.Authentication.ApiKey</c> package.
/// </summary>
/// <remarks>
/// Hashes the presented key with <see cref="ApiKeyHasher"/> and looks up the matching
/// <see cref="ApplicationUser"/> by its unique <c>ApiKeyHash</c> index. The lookup relies on
/// hashing being deterministic. Authentication fails closed: the expected EF Core lookup failures
/// (<see cref="DbUpdateException"/> and <see cref="InvalidOperationException"/>) are logged and
/// treated as no match; any other exception propagates to the authentication middleware, which
/// still results in failed (denied) authentication.
/// </remarks>
/// <param name="logger">The logger for diagnostic and error messages.</param>
/// <param name="userManager">The ASP.NET Core Identity user manager used to query users.</param>
/// <param name="hasher">The hasher used to convert a presented key into its stored hash form.</param>
public partial class ApiKeyProvider(
    ILogger<ApiKeyProvider> logger,
    UserManager<ApplicationUser> userManager,
    ApiKeyHasher hasher
) : IApiKeyProvider {

    /// <summary>
    /// Validates a presented API key and resolves it to an authenticated identity.
    /// </summary>
    /// <param name="key">The plaintext API key presented by the caller.</param>
    /// <returns>
    /// An <see cref="IApiKey"/> carrying the owner's username and a single <c>User</c> role claim
    /// when the key matches a user; otherwise <see langword="null"/> when no user matches or a
    /// data-access error occurs.
    /// </returns>
    public async Task<IApiKey?> ProvideAsync( string key ) {
        try {
            // Hash the provided key
            string hashedKey = hasher.HashApiKey( key );

            // Find user by hashed API key directly in the database
            ApplicationUser? user = await userManager.Users
                .FirstOrDefaultAsync( u => u.ApiKeyHash == hashedKey );

            return user == null
                ? null
                : (IApiKey)new ApiKey( key, user.UserName ?? user.Id, ["User"] );
        } catch (DbUpdateException ex) {
            LogDbError( ex );
            return null;
        } catch (InvalidOperationException ex) {
            LogInvalidOperation( ex );
            return null;
        }
    }

    /// <summary>
    /// Immutable <see cref="IApiKey"/> implementation describing a successfully resolved API key.
    /// </summary>
    /// <param name="key">The plaintext API key value.</param>
    /// <param name="ownerName">The username (or user id fallback) of the key owner.</param>
    /// <param name="roles">The role names to expose as role claims on the resolved identity.</param>
    private class ApiKey(
        string key,
        string ownerName,
        IReadOnlyCollection<string> roles
    ) : IApiKey {
        /// <summary>Gets the plaintext API key value.</summary>
        public string Key { get; } = key;

        /// <summary>Gets the username (or user id fallback) of the key owner.</summary>
        public string OwnerName { get; } = ownerName;

        /// <summary>Gets the claims granted to the resolved identity, one role claim per supplied role.</summary>
        public IReadOnlyCollection<Claim> Claims { get; } = [.. roles.Select( role => new Claim( ClaimTypes.Role, role ) )];
    }
}
