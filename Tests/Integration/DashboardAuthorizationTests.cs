using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BridgeBeats.Contracts.Constants;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for Aspire Dashboard authorization.
/// </summary>
[TestClass]
public class DashboardAuthorizationTests : IDisposable {
    private DashboardTestWebApplicationFactory? _factory;
    private HttpClient? _client;
    private const string TestUserEmail = "dashboardtest@example.com";
    private const string TestUserPassword = "TestPassword123!";

    /// <summary>
    /// Gets or sets the test context which provides information about and functionality for the current test run.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Initializes the test factory and HTTP client before each test.
    /// </summary>
    [TestInitialize]
    public async Task Setup( ) {
        // Load configuration from appsettings.json and user secrets
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile( Path.Combine( "src", "BridgeBeats.Web", "appsettings.json" ), optional: true )
            .AddUserSecrets<Web.Program>( optional: true )
            .AddEnvironmentVariables()
            .Build();

        // Build config overrides from loaded configuration
        Dictionary<string, string?> configData = configuration
            .AsEnumerable()
            .Where( kv => kv.Value is not null )
            .Where( kv => !kv.Key.EndsWith( "ConnectionString", StringComparison.OrdinalIgnoreCase ) )
            .ToDictionary( );

        // Force Discord token to null to prevent Discord service registration
        configData["BridgeBeats:DiscordToken"] = string.Empty;
        // Disable worker services mode - use direct provider implementations
        configData["BridgeBeats:Workers:UseWorkerServices"] = "false";
        // Force ATProto credentials to empty to disable caching service
        configData["BridgeBeats:ATProtoIdentifier"] = string.Empty;
        configData["BridgeBeats:ATProtoPassword"] = string.Empty;
        configData["BridgeBeats:ATProtoUserDID"] = string.Empty;

        _factory = new DashboardTestWebApplicationFactory( configData );
        _client = _factory.CreateClient( );

        // Initialize databases after services are configured
        await _factory.InitializeDatabasesAsync( );
    }

    /// <summary>
    /// Disposes of resources after each test.
    /// </summary>
    [TestCleanup]
    public void Cleanup( ) {
        Dispose( );
    }

    /// <summary>
    /// Disposes of the HTTP client and factory, cleaning up the in-memory database.
    /// </summary>
    public void Dispose( ) {
        _client?.Dispose( );
        _client = null;

        // Clean up the in-memory database
        // Note: We need to clean up before disposing the factory
        if (_factory != null) {
            try {
                using IServiceScope scope = _factory.Services.CreateScope( );
                ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>( );
                _ = dbContext.Database.EnsureDeleted( );
            } catch (ObjectDisposedException) {
                // Factory was already disposed, nothing to clean up
            }
            _factory.Dispose( );
            _factory = null;
        }

        GC.SuppressFinalize( this );
    }

    /// <summary>
    /// Tests that unauthenticated requests to dashboard authorization endpoint return 401.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DashboardAuthorize_UnauthenticatedUser_Returns401( ) {
        // Arrange
        using HttpClient client = _factory!.CreateClient( );

        // Act
        HttpResponseMessage response = await client.GetAsync( "/api/dashboard/authorize", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.Unauthorized, response.StatusCode );
    }

    /// <summary>
    /// Tests that authenticated users without the AspireDashboardAccess role return 403.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DashboardAuthorize_UserWithoutRole_Returns403( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: false );
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "TestScheme" );

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.Forbidden, response.StatusCode );
    }

    /// <summary>
    /// Tests that authenticated users with the AspireDashboardAccess role return 200.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task DashboardAuthorize_UserWithRole_Returns200( ) {
        // Arrange
        await CreateTestUserAsync( hasRole: true );
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "TestScheme" );

        // Act
        HttpResponseMessage response = await _client.GetAsync( "/api/dashboard/authorize", TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// Tests that the AspireDashboardAccess role is created on application startup.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
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

        // Store user info in the factory for the test auth handler
        _factory.SetTestUser( user.Id, TestUserEmail, hasRole );
    }

    /// <summary>
    /// Custom factory that adds test authentication scheme.
    /// </summary>
    private sealed class DashboardTestWebApplicationFactory(
        Dictionary<string, string?>? configOverrides
    ) : CustomWebApplicationFactory( configOverrides ) {
        private string? _testUserId;
        private string? _testUserEmail;
        private bool _testUserHasRole;

        /// <summary>
        /// Sets the test user information for authentication simulation.
        /// </summary>
        /// <param name="userId">The user ID to use for authentication.</param>
        /// <param name="email">The email address of the test user.</param>
        /// <param name="hasRole">Whether the test user has the AspireDashboardAccess role.</param>
        public void SetTestUser( string userId, string email, bool hasRole ) {
            _testUserId = userId;
            _testUserEmail = email;
            _testUserHasRole = hasRole;
        }

        protected override void ConfigureWebHost( IWebHostBuilder builder ) {
            base.ConfigureWebHost( builder );

            _ = builder.ConfigureServices( services => {
                // Add test authentication scheme
                _ = services.AddAuthentication( options => {
                    options.DefaultAuthenticateScheme = "TestScheme";
                    options.DefaultChallengeScheme = "TestScheme";
                } )
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>( "TestScheme", _ => { } );

                // Register this factory as a singleton so the handler can access it
                _ = services.AddSingleton( this );
            } );
        }

        /// <summary>
        /// Test authentication handler for integration tests.
        /// </summary>
        private sealed class TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            DashboardTestWebApplicationFactory factory
        ) : AuthenticationHandler<AuthenticationSchemeOptions>( options, logger, encoder ) {
            protected override Task<AuthenticateResult> HandleAuthenticateAsync( ) {
                // Only authenticate if Authorization header is present
                if (!Request.Headers.ContainsKey( "Authorization" )) {
                    return Task.FromResult( AuthenticateResult.NoResult( ) );
                }

                // Check if test user has been set
                if (string.IsNullOrEmpty( factory._testUserId ) || string.IsNullOrEmpty( factory._testUserEmail )) {
                    return Task.FromResult( AuthenticateResult.Fail( "No test user configured" ) );
                }

                List<Claim> claims = [
                    new Claim( ClaimTypes.NameIdentifier, factory._testUserId ),
                    new Claim( ClaimTypes.Name, factory._testUserEmail ),
                    new Claim( ClaimTypes.Email, factory._testUserEmail )
                ];

                // Add role claim if the test user has the role
                if (factory._testUserHasRole) {
                    claims.Add( new Claim( ClaimTypes.Role, Roles.AspireDashboardAccess ) );
                }

                ClaimsIdentity identity = new( claims, "TestScheme" );
                ClaimsPrincipal principal = new( identity );
                AuthenticationTicket ticket = new( principal, "TestScheme" );

                return Task.FromResult( AuthenticateResult.Success( ticket ) );
            }
        }
    }
}
