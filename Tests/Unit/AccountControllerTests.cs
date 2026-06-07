using BridgeBeats.Contracts.Records;
using BridgeBeats.Core.Infrastructure.Identity;
using BridgeBeats.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BridgeBeats.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="AccountController"/> covering paths that require mock injection,
/// specifically the <see cref="DbUpdateException"/> catch in <c>Register</c>.
/// </summary>
[TestClass]
public class AccountControllerTests {

    /// <summary>
    /// When <c>FindByEmailAsync</c> returns null (pre-check passes) but <c>CreateAsync</c> throws a
    /// <see cref="DbUpdateException"/> whose inner exception is a <see cref="SqliteException"/> with
    /// error code 19 and a message containing "AspNetUsers.NormalizedEmail", <c>Register</c> must
    /// return 400 with the DuplicateEmail description.
    ///
    /// The catch filter in AccountController.Register is:
    ///   catch (DbUpdateException dbEx)
    ///     when (dbEx.InnerException is SqliteException sqliteEx
    ///           &amp;&amp; sqliteEx.SqliteErrorCode == 19
    ///           &amp;&amp; sqliteEx.Message.Contains("AspNetUsers.NormalizedEmail", OrdinalIgnoreCase))
    ///
    /// Failure-first evidence: with the inner exception as InvalidOperationException (old test shape),
    /// the catch filter's type check (is SqliteException) evaluates to false, the exception propagates
    /// unhandled, and the test fails with "Expected BadRequestObjectResult but DbUpdateException was
    /// thrown." After replacing with SqliteException(error code 19, NormalizedEmail message),
    /// the filter matches and the controller returns 400.
    /// </summary>
    [TestMethod]
    public async Task Register_DbUniqueConstraintViolation_Returns400WithDuplicateEmailError( ) {
        // Arrange
        string email = "race@example.com";

        // Pre-check returns null (simulates: concurrent insertion won the race after the pre-check ran).
        // CreateAsync throws a DbUpdateException whose inner SqliteException matches all three guard
        // conditions: type SqliteException, error code 19, message contains AspNetUsers.NormalizedEmail.
        // Constructor: SqliteException(string message, int errorCode)
        DbUpdateException dbEx = new(
            "An error occurred while saving the entity changes.",
            new SqliteException( "SQLite Error 19: 'UNIQUE constraint failed: AspNetUsers.NormalizedEmail'.", 19 ) );

        FakeUserManager userManager = new(
            findByEmailResult: null,
            createAsyncException: dbEx
        );

        Mock<SignInManager<ApplicationUser>> signInManagerMock = BuildSignInManagerMock( userManager );

        ApiKeyHasher hasher = new( "test_salt" );
        AccountController controller = new(
            userManager,
            signInManagerMock.Object,
            NullLogger<AccountController>.Instance,
            hasher
        ) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext( )
            }
        };

        RegisterRequest request = new( email, "ValidPassword123!" );

        // Act
        IActionResult result = await controller.Register( request );

        // Assert
        _ = Assert.IsInstanceOfType<BadRequestObjectResult>( result,
            "A DbUpdateException with a SqliteException inner (code 19, NormalizedEmail column) must produce 400" );
    }

    /// <summary>
    /// Verifies the column-match narrowness of the catch filter: a <see cref="DbUpdateException"/>
    /// whose inner <see cref="SqliteException"/> has error code 19 but whose message names a DIFFERENT
    /// column (AspNetUsers.ApiKeyHash, not AspNetUsers.NormalizedEmail) must propagate — it is not
    /// swallowed by the email-specific catch guard.
    ///
    /// The catch filter requires all three conditions to hold simultaneously:
    ///   type SqliteException ✓, error code 19 ✓, message contains "AspNetUsers.NormalizedEmail" ✗
    /// The third condition fails, so the guard evaluates to false and the exception re-throws.
    ///
    /// Failure-first evidence: the filter is already present in the controller; this test verifies it
    /// does NOT fire for the wrong column. If the column-name check were removed from the filter, the
    /// catch would swallow this exception and return 400, causing this test to fail with
    /// "Expected DbUpdateException but got BadRequestObjectResult."
    /// </summary>
    [TestMethod]
    public async Task Register_SqliteExceptionWrongColumn_Propagates( ) {
        // Arrange
        string email = "race@example.com";

        // SqliteException code 19, but the message names a DIFFERENT column — must not be caught.
        DbUpdateException dbEx = new(
            "An error occurred while saving the entity changes.",
            new SqliteException( "SQLite Error 19: 'UNIQUE constraint failed: AspNetUsers.ApiKeyHash'.", 19 ) );

        FakeUserManager userManager = new(
            findByEmailResult: null,
            createAsyncException: dbEx
        );

        Mock<SignInManager<ApplicationUser>> signInManagerMock = BuildSignInManagerMock( userManager );

        ApiKeyHasher hasher = new( "test_salt" );
        AccountController controller = new(
            userManager,
            signInManagerMock.Object,
            NullLogger<AccountController>.Instance,
            hasher
        ) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext( )
            }
        };

        RegisterRequest request = new( email, "ValidPassword123!" );

        // Act + Assert — wrong-column SqliteException must propagate past the email catch guard
        _ = await Assert.ThrowsExactlyAsync<DbUpdateException>( ( ) => controller.Register( request ) );
    }

    /// <summary>
    /// Verifies the type narrowness of the catch filter: a <see cref="DbUpdateException"/> whose
    /// inner exception is NOT a <see cref="SqliteException"/> (e.g. <see cref="InvalidOperationException"/>)
    /// must propagate even when its message contains the NormalizedEmail column name.
    ///
    /// The catch filter requires all three conditions to hold simultaneously:
    ///   type SqliteException ✗ (InvalidOperationException), error code 19 n/a, NormalizedEmail text n/a
    /// The first condition fails, so the guard evaluates to false and the exception re-throws.
    ///
    /// Failure-first evidence: this is the old test shape that reproduced the CI failure. Before the
    /// controller's catch was tightened to require SqliteException, this same inner exception (an
    /// InvalidOperationException with the NormalizedEmail message) was matched by the broader filter
    /// and returned 400. After tightening, the type check fails and the exception propagates — which
    /// is the correct behavior this test asserts.
    /// </summary>
    [TestMethod]
    public async Task Register_NonSqliteInnerException_Propagates( ) {
        // Arrange
        string email = "race@example.com";

        // Inner exception is InvalidOperationException, not SqliteException — must not be caught.
        DbUpdateException dbEx = new(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException( "UNIQUE constraint failed: AspNetUsers.NormalizedEmail" ) );

        FakeUserManager userManager = new(
            findByEmailResult: null,
            createAsyncException: dbEx
        );

        Mock<SignInManager<ApplicationUser>> signInManagerMock = BuildSignInManagerMock( userManager );

        ApiKeyHasher hasher = new( "test_salt" );
        AccountController controller = new(
            userManager,
            signInManagerMock.Object,
            NullLogger<AccountController>.Instance,
            hasher
        ) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext( )
            }
        };

        RegisterRequest request = new( email, "ValidPassword123!" );

        // Act + Assert — non-SqliteException inner must propagate past the type-gated catch guard
        _ = await Assert.ThrowsExactlyAsync<DbUpdateException>( ( ) => controller.Register( request ) );
    }

    private static Mock<SignInManager<ApplicationUser>> BuildSignInManagerMock( UserManager<ApplicationUser> userManager ) {
        Mock<IHttpContextAccessor> contextAccessorMock = new( );
        Mock<IUserClaimsPrincipalFactory<ApplicationUser>> claimsFactoryMock = new( );
        return new Mock<SignInManager<ApplicationUser>>(
            userManager,
            contextAccessorMock.Object,
            claimsFactoryMock.Object,
            null!, null!, null!, null!
        );
    }

    /// <summary>
    /// Minimal test double for <see cref="UserManager{TUser}"/> that controls the return values of
    /// <c>FindByEmailAsync</c> and <c>CreateAsync</c> without Castle.DynamicProxy proxying, which
    /// is unreliable for abstract-constructored types with nullable parameters.
    /// </summary>
    private sealed class FakeUserManager(
        ApplicationUser? findByEmailResult,
        Exception? createAsyncException
    ) : UserManager<ApplicationUser>(
        new NoOpUserStore( ),
        null!, null!, null!, null!, null!,
        new IdentityErrorDescriber( ),
        null!, null!
    ) {
        public override Task<ApplicationUser?> FindByEmailAsync( string email )
            => Task.FromResult( findByEmailResult );

        public override Task<IdentityResult> CreateAsync( ApplicationUser user, string password ) {
            if (createAsyncException is not null) throw createAsyncException;
            return Task.FromResult( IdentityResult.Success );
        }

        /// <summary>
        /// Minimal no-op store — only the Create and FindByEmail paths are exercised in these tests.
        /// </summary>
        private sealed class NoOpUserStore : IUserStore<ApplicationUser> {
            public Task<IdentityResult> CreateAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );

            public Task<IdentityResult> DeleteAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );

            public void Dispose( ) { }

            public Task<ApplicationUser?> FindByIdAsync( string userId, CancellationToken ct )
                => Task.FromResult<ApplicationUser?>( null );

            public Task<ApplicationUser?> FindByNameAsync( string normalizedUserName, CancellationToken ct )
                => Task.FromResult<ApplicationUser?>( null );

            public Task<string?> GetNormalizedUserNameAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.NormalizedUserName );

            public Task<string> GetUserIdAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.Id ?? string.Empty );

            public Task<string?> GetUserNameAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.UserName );

            public Task SetNormalizedUserNameAsync( ApplicationUser user, string? normalizedName, CancellationToken ct ) {
                user.NormalizedUserName = normalizedName;
                return Task.CompletedTask;
            }

            public Task SetUserNameAsync( ApplicationUser user, string? userName, CancellationToken ct ) {
                user.UserName = userName;
                return Task.CompletedTask;
            }

            public Task<IdentityResult> UpdateAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );
        }
    }
}
