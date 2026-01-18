using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for Aspire Dashboard authorization.
/// </summary>
[TestClass]
public class DashboardAuthorizationTests : IDisposable {
    private CustomWebApplicationFactory? _factory;
    private HttpClient? _client;
    private const string TestUserEmail = "dashboardtest@example.com";
    private const string TestUserPassword = "TestPassword123!";

    [TestInitialize]
    public async Task Setup( ) {
        // Use minimal config - let factory provide test defaults for Spotify
        Dictionary<string, string?> configData = new( ) {
            // Force Discord token to empty string to prevent Discord service registration
            ["BridgeBeats:DiscordToken"] = ""
        };

        _factory = new CustomWebApplicationFactory( configData );
        _client = _factory.CreateClient( );

        // Initialize databases after services are configured
        await _factory.InitializeDatabasesAsync( );
    }

    [TestCleanup]
    public void Cleanup( ) {
        Dispose( );
    }

    public void Dispose( ) {
        _client?.Dispose( );
        _factory?.Dispose( );
        GC.SuppressFinalize( this );
    }

    /// <summary>
    /// Tests that unauthenticated requests to dashboard authorization endpoint return 401.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UnauthenticatedUser_Returns401( ) {
        // Arrange
        using HttpClient client = _factory!.CreateClient( );

        // Act
        HttpResponseMessage response = await client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode );
    }

    /// <summary>
    /// Tests that authenticated users without the AspireDashboardAccess role return 403.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UserWithoutRole_Returns403( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: false );
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "TestScheme" );

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        Assert.AreEqual( HttpStatusCode.Forbidden, response.StatusCode );
    }

    /// <summary>
    /// Tests that authenticated users with the AspireDashboardAccess role return 200.
    /// </summary>
    [TestMethod]
    public async Task DashboardAuthorize_UserWithRole_Returns200( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: true );
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "TestScheme" );

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize" );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Tests that the AspireDashboardAccess role is created on application startup.
    /// </summary>
    [TestMethod]
    public async Task DatabaseInitialization_CreatesAspireDashboardAccessRole( ) {
        // Arrange
        using IServiceScope scope = _factory!.Services.CreateScope( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>( );

        // Act
        bool roleExists = await roleManager.RoleExistsAsync( Roles.AspireDashboardAccess );

        // Assert
        Assert.IsTrue( roleExists );
    }

    /// <summary>
    /// Helper method to create a test user with or without the AspireDashboardAccess role.
    /// </summary>
    private async Task CreateTestUserAsync( bool hasRole ) {
        using IServiceScope scope = _factory!.Services.CreateScope( );
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>( );
        RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>( );

        // Create role if it doesn't exist
        if (!await roleManager.RoleExistsAsync( Roles.AspireDashboardAccess )) {
            _ = await roleManager.CreateAsync( new IdentityRole( Roles.AspireDashboardAccess ) );
        }

        // Create test user
        ApplicationUser user = new( ) {
            UserName = TestUserEmail,
            Email = TestUserEmail,
            EmailConfirmed = true
        };

        IdentityResult result = await userManager.CreateAsync( user, TestUserPassword );
        if (!result.Succeeded) {
            throw new InvalidOperationException( "Failed to create test user: " + string.Join( ", ", result.Errors.Select( e => e.Description ) ) );
        }

        // Assign role if requested
        if (hasRole) {
            _ = await userManager.AddToRoleAsync( user, Roles.AspireDashboardAccess );
        }
    }

    /// <summary>
    /// Test authentication handler for integration tests.
    /// </summary>
    public class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>( options, logger, encoder ) {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync( ) {
            Claim[] claims = [
                new Claim( ClaimTypes.Name, TestUserEmail ),
                new Claim( ClaimTypes.Email, TestUserEmail )
            ];
            ClaimsIdentity identity = new( claims, "TestScheme" );
            ClaimsPrincipal principal = new( identity );
            AuthenticationTicket ticket = new( principal, "TestScheme" );

            return Task.FromResult( AuthenticateResult.Success( ticket ) );
        }
    }
}
