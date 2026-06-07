using System.Net;
using System.Text;
using System.Text.Json;
using BridgeBeats.Contracts.Interfaces;
using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

#pragma warning disable CS1591

namespace BridgeBeats.Tests.Integration;

/// <summary>
/// Integration tests for the ATProto login fix (Bug 2) and Register duplicate-email check.
/// Uses <see cref="CustomWebApplicationFactory"/> with a stubbed <see cref="IATProtoOAuthService"/>.
/// </summary>
/// <remarks>
/// [DoNotParallelize]: the factory ctor sets process-wide env vars (BridgeBeats__Workers__UseWorkerServices,
/// BridgeBeats__SpotifyClientId/Secret) and restores them in Dispose. Running in parallel with
/// another class that mutates overlapping keys would cause order-dependent contamination.
/// </remarks>
[TestClass]
[DoNotParallelize]
public class AtProtoLoginIntegrationTests : IDisposable {

    private AtProtoTestWebApplicationFactory? _factory;
    private HttpClient? _client;

    private const string TestHandle = "taylormar.vin";
    private const string TestDid = "did:plc:kx2mxhedzbeuqethywrzdexz";
    private const string TestAccessToken = "test-access-token";
    private const string TestRefreshToken = "test-refresh-token";
    private const string TestDPoPKey = "{\"kty\":\"EC\",\"crv\":\"P-256\"}";
    private static readonly DateTime s_testTokenExpiration = DateTime.UtcNow.AddHours( 1 );

    /// <summary>
    /// Gets or sets the test context which provides cooperative cancellation support.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task Setup( ) {
        _factory = new AtProtoTestWebApplicationFactory( );
        _client = _factory.CreateClient( new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false
        } );
        await _factory.InitializeDatabasesAsync( );
    }

    [TestCleanup]
    public void Cleanup( ) => Dispose( );

    /// <inheritdoc/>
    public void Dispose( ) {
        _client?.Dispose( );
        _client = null;
        _factory?.Dispose( );
        _factory = null;
        GC.SuppressFinalize( this );
    }

    /// <summary>
    /// Register with a malformed email must return 400 (format validation).
    ///
    /// Failure-first evidence: before adding [EmailAddress] to RegisterRequest, POST /account/register
    /// with email="notanemail" returned 200 — ModelState was valid and the user was created (Identity
    /// accepts any username as long as it is unique). After adding [EmailAddress], MVC populates
    /// ModelState with a format error and the manual !ModelState.IsValid check returns 400.
    /// Confirmed by removing the [EmailAddress] annotation: the POST returns 200.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Register_WithMalformedEmail_Returns400( ) {
        string antiforgery = await AntiforgeryTestHelper.GetAntiforgeryTokenAsync( _client!, TestContext.CancellationToken );

        StringContent content = new(
            JsonSerializer.Serialize( new { email = "notanemail", password = "ValidPassword123!" } ),
            Encoding.UTF8,
            "application/json"
        );
        _client!.DefaultRequestHeaders.Add( "X-XSRF-TOKEN", antiforgery );

        HttpResponseMessage response = await _client.PostAsync( "/account/register", content, TestContext.CancellationToken );

        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode, "Malformed email must produce 400" );
    }

    /// <summary>
    /// Register with an email already in use must return 400 DuplicateEmail.
    ///
    /// Failure-first evidence: the pre-existing user is created with a DIFFERENT UserName so that
    /// Identity's username-uniqueness check does not produce the 400 — only the explicit
    /// FindByEmailAsync check in Register discriminates. Verified by commenting out the
    /// FindByEmailAsync check: the second POST /account/register returns 200 (CreateAsync succeeds
    /// because RequireUniqueEmail=false and the username is unique). With the check in place, it
    /// returns 400 with Identity's DuplicateEmail description.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task Register_WithDuplicateEmail_Returns400( ) {
        // Arrange — create the first user via UserManager with a DIFFERENT UserName than the email.
        // This is deliberate: Identity always enforces username uniqueness, so if UserName == email,
        // a 400 fires even without the explicit FindByEmailAsync check — giving false coverage.
        // Using a distinct UserName ensures only the email-uniqueness path discriminates.
        using IServiceScope scope = _factory!.Services.CreateScope( );
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>( );

        string existingEmail = "duplicate@example.com";
        ApplicationUser existing = new( ) {
            UserName = "existing-user-different-username",
            Email = existingEmail,
            EmailConfirmed = true
        };
        IdentityResult createResult = await userManager.CreateAsync( existing, "TestPassword123!" );
        Assert.IsTrue( createResult.Succeeded, "Setup: first user creation should succeed" );

        // Arrange — try to register a second user with the same email but a fresh UserName (== email,
        // as Register always sets UserName = request.Email).
        string antiforgery = await AntiforgeryTestHelper.GetAntiforgeryTokenAsync( _client!, TestContext.CancellationToken );

        StringContent content = new(
            JsonSerializer.Serialize( new { email = existingEmail, password = "AnotherPassword123!" } ),
            Encoding.UTF8,
            "application/json"
        );
        _client!.DefaultRequestHeaders.Add( "X-XSRF-TOKEN", antiforgery );

        // Act
        HttpResponseMessage response = await _client.PostAsync( "/account/register", content, TestContext.CancellationToken );

        // Assert
        Assert.AreEqual( HttpStatusCode.BadRequest, response.StatusCode );
        string body = await response.Content.ReadAsStringAsync( TestContext.CancellationToken );
        Assert.IsTrue(
            body.Contains( "already taken", StringComparison.OrdinalIgnoreCase ) ||
            body.Contains( "DuplicateEmail", StringComparison.OrdinalIgnoreCase ),
            $"Expected duplicate-email wording in: {body}"
        );
    }

    /// <summary>
    /// Full ATProto callback happy path with a stubbed IATProtoOAuthService:
    /// - user is created in the database
    /// - response is a redirect to /
    /// - the new user's ApiKeyHash IS null (ATProto users receive no API key at creation time;
    ///   the plaintext would be discarded at the OAuth redirect)
    /// - a sign-in cookie is issued
    ///
    /// Failure-first evidence: on the pre-fix code (RequireUniqueEmail = true), userManager.CreateAsync
    /// fails with "Email '' is invalid." The callback redirects to /account/login?error=... instead of
    /// returning a redirect to /. After the fix, the user is created and the redirect goes to /.
    /// The ApiKeyHash assertion changed from non-null to null per M1 director ratification: ATProto
    /// users mint keys via POST /account/regenerate-api-key after sign-in.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task AtProtoCallback_NewUser_CreatesUserWithNullApiKeyHashAndRedirects( ) {
        // Arrange — stub returns a valid OAuth result
        _factory!.SetupOAuthResult( new ATProtoOAuthResult {
            Did = TestDid,
            Handle = TestHandle,
            AccessToken = TestAccessToken,
            RefreshToken = TestRefreshToken,
            DPoPKeyJwk = TestDPoPKey,
            TokenExpiration = s_testTokenExpiration,
            Scope = "atproto transition:generic"
        } );

        string callbackUrl = $"/account/atproto-callback?code=testcode&state={_factory.TestState}&iss=https%3A%2F%2Fbsky.social";

        // Act
        HttpResponseMessage response = await _client!.GetAsync( callbackUrl, TestContext.CancellationToken );

        // Assert — redirect to /
        Assert.AreEqual( HttpStatusCode.Redirect, response.StatusCode, $"Expected redirect, got {response.StatusCode}" );
        Assert.AreEqual( "/", response.Headers.Location?.OriginalString, "Expected redirect to /" );

        // Assert — sign-in cookie was issued
        bool hasCookie = response.Headers.TryGetValues( "Set-Cookie", out IEnumerable<string>? cookies )
            && cookies.Any( c => c.Contains( ".AspNetCore.Identity.Application", StringComparison.OrdinalIgnoreCase ) );
        Assert.IsTrue( hasCookie, "Expected an ASP.NET Core auth cookie to be set" );

        // Assert — user was created in the database with a null ApiKeyHash.
        // ATProto users have no API key at creation time: the plaintext would be discarded at the
        // OAuth redirect. Keys are minted post-login via POST /account/regenerate-api-key.
        using IServiceScope scope = _factory.Services.CreateScope( );
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>( );
        ApplicationUser? createdUser = await userManager.Users.FirstOrDefaultAsync(
            u => u.AtProtoDid == TestDid,
            TestContext.CancellationToken
        );

        Assert.IsNotNull( createdUser, "User should have been created in the database" );
        Assert.IsTrue( string.IsNullOrEmpty( createdUser.ApiKeyHash ), "New ATProto user must have null ApiKeyHash — key is minted later via /account/regenerate-api-key" );
    }

    /// <summary>
    /// Second ATProto callback for the same DID takes the update path — no duplicate user is created.
    ///
    /// Failure-first evidence: this test always passed (update path was not affected by the bug), but
    /// it guards against a regression where the create path might run a second time for a known DID.
    /// Confirmed by running it with assertions: userManager.Users.Count(u => u.AtProtoDid == did) == 1.
    /// </summary>
    [TestMethod]
    [Timeout( 30000, CooperativeCancellation = true )]
    public async Task AtProtoCallback_ExistingUser_UpdatesTokensNoDuplicateUser( ) {
        // Arrange — pre-create the user
        using (IServiceScope setupScope = _factory!.Services.CreateScope( )) {
            UserManager<ApplicationUser> userManager = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>( );
            ApplicationUser existing = new( ) {
                UserName = TestHandle,
                AtProtoDid = TestDid,
                AtProtoHandle = TestHandle,
                AtProtoAccessToken = "old-access-token",
                AtProtoRefreshToken = "old-refresh-token",
                AtProtoDPoPKey = TestDPoPKey,
                AtProtoTokenExpiration = DateTime.UtcNow.AddHours( -1 ),
                CreatedAt = DateTime.UtcNow.AddDays( -1 )
            };
            IdentityResult createResult = await userManager.CreateAsync( existing );
            Assert.IsTrue( createResult.Succeeded, $"Setup: {string.Join( ", ", createResult.Errors.Select( e => e.Description ) )}" );
        }

        _factory.SetupOAuthResult( new ATProtoOAuthResult {
            Did = TestDid,
            Handle = TestHandle,
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token",
            DPoPKeyJwk = TestDPoPKey,
            TokenExpiration = s_testTokenExpiration,
            Scope = "atproto transition:generic"
        } );

        string callbackUrl = $"/account/atproto-callback?code=testcode&state={_factory.TestState}&iss=https%3A%2F%2Fbsky.social";

        // Act
        HttpResponseMessage response = await _client!.GetAsync( callbackUrl, TestContext.CancellationToken );

        // Assert — still redirects to /
        Assert.AreEqual( HttpStatusCode.Redirect, response.StatusCode );
        Assert.AreEqual( "/", response.Headers.Location?.OriginalString );

        // Assert — exactly one user with this DID
        using IServiceScope verifyScope = _factory.Services.CreateScope( );
        UserManager<ApplicationUser> verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>( );
        int userCount = verifyUserManager.Users.Count( u => u.AtProtoDid == TestDid );
        Assert.AreEqual( 1, userCount, $"Expected exactly 1 user with DID {TestDid}, found {userCount}" );

        // Assert — tokens were updated
        ApplicationUser? updated = verifyUserManager.Users.FirstOrDefault( u => u.AtProtoDid == TestDid );
        Assert.IsNotNull( updated );
        Assert.AreEqual( "new-access-token", updated.AtProtoAccessToken );
    }

    /// <summary>
    /// Custom factory that stubs IATProtoOAuthService for integration testing.
    /// </summary>
    private sealed class AtProtoTestWebApplicationFactory : CustomWebApplicationFactory, IDisposable {
        private readonly Mock<IATProtoOAuthService> _oauthMock = new( );
        public readonly string TestState = Guid.NewGuid( ).ToString( "N" );

        // Env var names that this factory mutates and must restore on dispose.
        private static readonly (string EnvVar, string? PriorValue)[] s_envVarRestoreList = [
            ("BridgeBeats__Workers__UseWorkerServices",
             Environment.GetEnvironmentVariable( "BridgeBeats__Workers__UseWorkerServices" )),
            ("BridgeBeats__SpotifyClientId",
             Environment.GetEnvironmentVariable( "BridgeBeats__SpotifyClientId" )),
            ("BridgeBeats__SpotifyClientSecret",
             Environment.GetEnvironmentVariable( "BridgeBeats__SpotifyClientSecret" )),
            ("BridgeBeats__AppleTeamId",
             Environment.GetEnvironmentVariable( "BridgeBeats__AppleTeamId" )),
            ("BridgeBeats__AppleKeyId",
             Environment.GetEnvironmentVariable( "BridgeBeats__AppleKeyId" )),
            ("BridgeBeats__AppleKeyPath",
             Environment.GetEnvironmentVariable( "BridgeBeats__AppleKeyPath" )),
        ];

        private static readonly Dictionary<string, string?> s_configOverrides = new( ) {
            // In-memory config-data overrides (DashboardAuthorizationTests.cs:54 pattern).
            // These are the primary override path; env vars below are a belt-and-braces guard
            // for environments where user secrets or host env vars would otherwise set
            // UseWorkerServices=true or supply non-test Spotify credentials before the in-memory
            // config source is added. See CustomWebApplicationFactory comment for full explanation.
            ["BridgeBeats:Workers:UseWorkerServices"] = "false",
            ["BridgeBeats:DiscordToken"] = "",
            ["BridgeBeats:ATProtoIdentifier"] = "",
            ["BridgeBeats:ATProtoPassword"] = "",
            ["BridgeBeats:ATProtoUserDID"] = "",
            // Blank Apple credentials so AddAppleMusicJwtHandler skips File.ReadAllText during
            // service registration — prevents UnauthorizedAccessException on machines where Apple
            // Music user secrets or host env vars supply a real key path.
            ["BridgeBeats:AppleTeamId"] = "",
            ["BridgeBeats:AppleKeyId"] = "",
            ["BridgeBeats:AppleKeyPath"] = "",
            // Provide Spotify credentials so at least one IMusicLookupService is registered
            ["BridgeBeats:SpotifyClientId"] = "test",
            ["BridgeBeats:SpotifyClientSecret"] = "test"
        };

        public AtProtoTestWebApplicationFactory( ) : base( s_configOverrides ) {
            // Belt-and-braces: also set env vars so these values are visible during the earliest
            // configuration-binding phase (before ConfigureAppConfiguration callbacks run).
            // Prior values are captured in s_envVarRestoreList and restored in Dispose().
            Environment.SetEnvironmentVariable( "BridgeBeats__Workers__UseWorkerServices", "false" );
            Environment.SetEnvironmentVariable( "BridgeBeats__SpotifyClientId", "test" );
            Environment.SetEnvironmentVariable( "BridgeBeats__SpotifyClientSecret", "test" );
            // Belt-and-braces for Apple: must blank env vars before base() (Program.Main) reads them.
            // AddAppleMusicJwtHandler calls File.ReadAllText during service registration; if any of
            // these three env vars is non-empty on the host, the handler attempts to open the key file.
            Environment.SetEnvironmentVariable( "BridgeBeats__AppleTeamId", "" );
            Environment.SetEnvironmentVariable( "BridgeBeats__AppleKeyId", "" );
            Environment.SetEnvironmentVariable( "BridgeBeats__AppleKeyPath", "" );

            // Default: CompleteAuthorizationAsync returns a failure so tests must call SetupOAuthResult
            _ = _oauthMock
                .Setup( s => s.CompleteAuthorizationAsync(
                    It.IsAny<string>( ),
                    It.IsAny<string>( ),
                    It.IsAny<string>( ),
                    It.IsAny<CancellationToken>( ) ) )
                .ThrowsAsync( new InvalidOperationException( "No OAuth result configured for this test" ) );
        }

        /// <inheritdoc/>
        protected override void Dispose( bool disposing ) {
            // Restore env vars to prevent process-wide contamination of parallel test classes.
            foreach ((string envVar, string? prior) in s_envVarRestoreList) {
                Environment.SetEnvironmentVariable( envVar, prior );
            }
            base.Dispose( disposing );
        }

        public void SetupOAuthResult( ATProtoOAuthResult result ) {
            _ = _oauthMock
                .Setup( s => s.CompleteAuthorizationAsync(
                    TestState,
                    It.IsAny<string>( ),
                    It.IsAny<string>( ),
                    It.IsAny<CancellationToken>( ) ) )
                .ReturnsAsync( result );
        }

        protected override void ConfigureWebHost( IWebHostBuilder builder ) {
            base.ConfigureWebHost( builder );

            _ = builder.ConfigureServices( services => {
                _ = services.RemoveAll<IATProtoOAuthService>( );
                _ = services.AddSingleton( _oauthMock.Object );
            } );
        }
    }
}

#pragma warning restore CS1591
