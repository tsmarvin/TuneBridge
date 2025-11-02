using AspNetCore.Authentication.ApiKey;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TuneBridge.Domain.Models;

namespace TuneBridge.Domain.Implementations.Auth;

/// <summary>
/// Provides API key authentication by validating keys against user records in the database.
/// </summary>
public class ApiKeyProvider : IApiKeyProvider {
    private readonly ILogger<ApiKeyProvider> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApiKeyHasher _hasher;

    public ApiKeyProvider( ILogger<ApiKeyProvider> logger, UserManager<ApplicationUser> userManager, ApiKeyHasher hasher ) {
        _logger = logger;
        _userManager = userManager;
        _hasher = hasher;
    }

    public async Task<IApiKey?> ProvideAsync( string key ) {
        try {
            // Hash the provided key
            string hashedKey = _hasher.HashApiKey( key );

            // Find user by hashed API key directly in the database
            ApplicationUser? user = await _userManager.Users
                .FirstOrDefaultAsync( u => u.ApiKeyHash == hashedKey );

            if (user == null) {
                return null;
            }

            return new ApiKey( key, user.UserName ?? user.Id, [ "User" ] );
        } catch (Exception ex) {
            _logger.LogError( ex, "Error validating API key" );
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
