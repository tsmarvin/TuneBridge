using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TuneBridge.Domain.Implementations.Auth;
using TuneBridge.Domain.Models;

namespace TuneBridge.Web.Controllers;

/// <summary>
/// Controller for user authentication and account management.
/// Provides endpoints for user registration, login, and API key generation.
/// </summary>
[ApiController]
[Route( "account" )]
public class AccountController : ControllerBase {
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<AccountController> _logger;
    private readonly ApiKeyHasher _hasher;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<AccountController> logger,
        ApiKeyHasher hasher ) {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
        _hasher = hasher;
    }

    /// <summary>Request for user registration.</summary>
    /// <param name="Email">User email address.</param>
    /// <param name="Password">User password.</param>
    /// <param name="Username">Optional username.</param>
    public record RegisterRequest( string Email, string Password, string? Username );

    /// <summary>Request for user login.</summary>
    /// <param name="Email">User email address.</param>
    /// <param name="Password">User password.</param>
    public record LoginRequest( string Email, string Password );

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    /// <param name="request">Registration details including email and password.</param>
    /// <returns>User details with API key on success.</returns>
    /// <response code="200">User successfully registered.</response>
    /// <response code="400">Registration failed (validation errors or duplicate user).</response>
    [HttpPost( "register" )]
    public async Task<IActionResult> Register( [FromBody] RegisterRequest request ) {
        if (!ModelState.IsValid) {
            return BadRequest( ModelState );
        }

        // Generate API key
        string apiKey = ApiKeyHasher.GenerateApiKey( );
        string hashedApiKey = _hasher.HashApiKey( apiKey );

        ApplicationUser user = new() {
            UserName = request.Username ?? request.Email,
            Email = request.Email,
            ApiKeyHash = hashedApiKey,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult result = await _userManager.CreateAsync( user, request.Password );

        if (!result.Succeeded) {
            foreach (IdentityError error in result.Errors) {
                ModelState.AddModelError( string.Empty, error.Description );
            }
            return BadRequest( ModelState );
        }

        _logger.LogInformation( "User registered successfully with ID: {UserId}", user.Id );

        return Ok( new {
            userId = user.Id,
            apiKey = apiKey,
            message = "Registration successful. Save your API key - it will not be shown again."
        } );
    }

    /// <summary>
    /// Authenticates a user and returns a new API key.
    /// </summary>
    /// <param name="request">Login credentials.</param>
    /// <returns>User details with new API key on success.</returns>
    /// <response code="200">Login successful.</response>
    /// <response code="401">Invalid credentials.</response>
    [HttpPost( "login" )]
    public async Task<IActionResult> Login( [FromBody] LoginRequest request ) {
        if (!ModelState.IsValid) {
            return BadRequest( ModelState );
        }

        ApplicationUser? user = await _userManager.FindByEmailAsync( request.Email );
        if (user == null) {
            return Unauthorized( new { message = "Invalid email or password" } );
        }

        Microsoft.AspNetCore.Identity.SignInResult result = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: false
        );

        if (!result.Succeeded) {
            return Unauthorized( new { message = "Invalid email or password" } );
        }

        // Generate new API key for this login session
        string apiKey = ApiKeyHasher.GenerateApiKey( );
        user.ApiKeyHash = _hasher.HashApiKey( apiKey );
        await _userManager.UpdateAsync( user );

        _logger.LogInformation( "User logged in successfully with ID: {UserId}", user.Id );

        return Ok( new {
            userId = user.Id,
            apiKey = apiKey,
            message = "Login successful. New API key generated."
        } );
    }

    /// <summary>
    /// Regenerates the API key for the authenticated user.
    /// </summary>
    /// <returns>New API key.</returns>
    /// <response code="200">API key regenerated successfully.</response>
    /// <response code="401">User not authenticated.</response>
    [Authorize]
    [HttpPost( "regenerate-api-key" )]
    public async Task<IActionResult> RegenerateApiKey( ) {
        ApplicationUser? user = await _userManager.GetUserAsync( User );
        if (user == null) {
            return Unauthorized( );
        }

        string apiKey = ApiKeyHasher.GenerateApiKey( );
        user.ApiKeyHash = _hasher.HashApiKey( apiKey );
        IdentityResult result = await _userManager.UpdateAsync( user );

        if (!result.Succeeded) {
            return BadRequest( new { message = "Failed to regenerate API key" } );
        }

        _logger.LogInformation( "User regenerated API key with ID: {UserId}", user.Id );

        return Ok( new {
            apiKey = apiKey,
            message = "API key regenerated successfully. Update your applications with the new key."
        } );
    }
}
