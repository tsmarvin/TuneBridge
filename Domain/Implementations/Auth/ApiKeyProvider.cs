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

    public ApiKeyProvider( ILogger<ApiKeyProvider> logger, UserManager<ApplicationUser> userManager ) {
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<IApiKey?> ProvideAsync( string key ) {
        try {
            // Find user by API key
            ApplicationUser? user = ( await _userManager.Users
                .Where( u => u.ApiKey == key )
                .ToListAsync( ) )
                .FirstOrDefault( );

            if (user == null) {
                return null;
            }

            // Update last request timestamp
            user.LastRequestAt = DateTime.UtcNow;
            _ = await _userManager.UpdateAsync( user );

            return new ApiKey( key, user.UserName ?? user.Email ?? "Unknown", [ "User" ] );
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
