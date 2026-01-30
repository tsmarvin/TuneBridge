using System.Security.Claims;
using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Core.Infrastructure.Identity;

/// <summary>
/// Provides API key authentication by validating keys against user records in the database.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ApiKeyProvider"/> class.
/// </remarks>
/// <param name="logger">Logger for diagnostics and error tracking.</param>
/// <param name="userManager">User manager for database queries.</param>
/// <param name="hasher">API key hasher for secure key validation.</param>
public partial class ApiKeyProvider(
    ILogger<ApiKeyProvider> logger,
    UserManager<ApplicationUser> userManager,
    ApiKeyHasher hasher
) : IApiKeyProvider {

    /// <summary>
    /// Validates an API key and returns the associated authenticated user context.
    /// </summary>
    /// <param name="key">The raw API key to validate.</param>
    /// <returns>An authenticated API key context if valid, null otherwise.</returns>
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
    /// Internal implementation of IApiKey for authentication.
    /// </summary>
    private class ApiKey(
        string key,
        string ownerName,
        IReadOnlyCollection<string> roles
    ) : IApiKey {
        public string Key { get; } = key;
        public string OwnerName { get; } = ownerName;
        public IReadOnlyCollection<Claim> Claims { get; } = [.. roles.Select( role => new Claim( ClaimTypes.Role, role ) )];
    }
}
