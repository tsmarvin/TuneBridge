using AspNetCore.Authentication.ApiKey;
using BridgeBeats.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BridgeBeats.Domain.Implementations.Auth;

/// <summary>
/// Provides API key authentication by validating keys against user records in the database.
/// </summary>
public class ApiKeyProvider : IApiKeyProvider {
    private readonly ILogger<ApiKeyProvider> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApiKeyHasher _hasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostics and error tracking.</param>
    /// <param name="userManager">User manager for database queries.</param>
    /// <param name="hasher">API key hasher for secure key validation.</param>
    public ApiKeyProvider( ILogger<ApiKeyProvider> logger, UserManager<ApplicationUser> userManager, ApiKeyHasher hasher ) {
        _logger = logger;
        _userManager = userManager;
        _hasher = hasher;
    }

    /// <summary>
    /// Validates an API key and returns the associated authenticated user context.
    /// </summary>
    /// <param name="key">The raw API key to validate.</param>
    /// <returns>An authenticated API key context if valid, null otherwise.</returns>
    public async Task<IApiKey?> ProvideAsync( string key ) {
        try {
            // Hash the provided key
            string hashedKey = _hasher.HashApiKey( key );

            // Find user by hashed API key directly in the database
            ApplicationUser? user = await _userManager.Users
                .FirstOrDefaultAsync( u => u.ApiKeyHash == hashedKey );

            return user == null
                ? null
                : (IApiKey)new ApiKey( key, user.UserName ?? user.Id, ["User"] );
        } catch (DbUpdateException ex) {
            _logger.LogError( ex, "Database error while validating API key" );
            return null;
        } catch (InvalidOperationException ex) {
            _logger.LogError( ex, "Invalid operation while validating API key" );
            return null;
        }
    }

    /// <summary>
    /// Internal implementation of IApiKey for authentication.
    /// </summary>
    private class ApiKey : IApiKey {
        public ApiKey( string key, string ownerName, IReadOnlyCollection<string> roles ) {
            Key = key;
            OwnerName = ownerName;
            Claims = roles.Select( role => new System.Security.Claims.Claim( System.Security.Claims.ClaimTypes.Role, role ) ).ToList( );
        }

        public string Key { get; }
        public string OwnerName { get; }
        public IReadOnlyCollection<System.Security.Claims.Claim> Claims { get; }
    }
}
