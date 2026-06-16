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
/// Tests <see cref="AccountController.Register"/>'s handling of database-level errors raised when a
/// user registration is persisted, focusing on how the controller distinguishes a duplicate-email
/// unique-constraint violation from other failures.
/// </summary>
/// <remarks>
/// The DB enforces email uniqueness through a unique index on <c>NormalizedEmail</c> even though
/// ASP.NET Identity's own <c>RequireUniqueEmail</c> is disabled, so a concurrent duplicate
/// registration surfaces as a <see cref="DbUpdateException"/> wrapping a SQLite error 19 rather than
/// being caught by Identity validation. These tests assert the controller maps that specific case to
/// a 400 and re-throws everything else.
/// </remarks>
[TestClass]
public class AccountControllerTests {

    /// <summary>
    /// Verifies that a <see cref="DbUpdateException"/> whose inner <see cref="SqliteException"/> reports
    /// a unique-constraint violation on the <c>NormalizedEmail</c> column (SQLite error code 19) is
    /// translated into a <see cref="BadRequestObjectResult"/> rather than propagating, so a racing
    /// duplicate registration becomes a clean 400 for the caller.
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
    /// Verifies that a <see cref="DbUpdateException"/> wrapping a <see cref="SqliteException"/> for a
    /// unique-constraint violation on a column other than <c>NormalizedEmail</c> (here <c>ApiKeyHash</c>)
    /// is re-thrown rather than converted to a 400, confirming the duplicate-email mapping is scoped to
    /// the email column alone.
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
    /// Verifies that a <see cref="DbUpdateException"/> whose inner exception is not a
    /// <see cref="SqliteException"/> (here an <see cref="InvalidOperationException"/>, even though its
    /// message text mentions the email constraint) is re-thrown, confirming the controller keys off the
    /// inner exception type and not on message-string matching.
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

    /// <summary>
    /// Builds a <see cref="Mock{T}"/> of <see cref="SignInManager{TUser}"/> over the supplied user
    /// manager, supplying the constructor dependencies the sign-in manager requires so an
    /// <see cref="AccountController"/> can be constructed without a live Identity stack.
    /// </summary>
    /// <param name="userManager">The user manager the mocked sign-in manager wraps.</param>
    /// <returns>A mock sign-in manager suitable for injecting into the controller under test.</returns>
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
    /// Test double for <see cref="UserManager{TUser}"/> that returns a canned lookup result and
    /// optionally throws a configured exception from <see cref="CreateAsync"/>, letting each test drive
    /// the controller down a specific persistence-failure branch without a real Identity store.
    /// Avoids Castle.DynamicProxy proxying, which is unreliable for abstract-constructored types with
    /// nullable parameters.
    /// </summary>
    /// <param name="findByEmailResult">The value returned from <see cref="FindByEmailAsync"/>.</param>
    /// <param name="createAsyncException">
    /// The exception thrown from <see cref="CreateAsync"/>, or <see langword="null"/> to report success.
    /// </param>
    private sealed class FakeUserManager(
        ApplicationUser? findByEmailResult,
        Exception? createAsyncException
    ) : UserManager<ApplicationUser>(
        new NoOpUserStore( ),
        null!, null!, null!, null!, null!,
        new IdentityErrorDescriber( ),
        null!, null!
    ) {
        /// <summary>Returns the configured user lookup result regardless of the email argument.</summary>
        /// <param name="email">The email to look up (ignored; the canned result is returned).</param>
        /// <returns>The pre-configured <see cref="ApplicationUser"/> result, possibly <see langword="null"/>.</returns>
        public override Task<ApplicationUser?> FindByEmailAsync( string email )
            => Task.FromResult( findByEmailResult );

        /// <summary>
        /// Throws the configured exception to simulate a persistence failure, or reports success when no
        /// exception was configured.
        /// </summary>
        /// <param name="user">The user being created (unused).</param>
        /// <param name="password">The password supplied for the new user (unused).</param>
        /// <returns>A successful <see cref="IdentityResult"/> when no failure was configured.</returns>
        public override Task<IdentityResult> CreateAsync( ApplicationUser user, string password ) {
            if (createAsyncException is not null) throw createAsyncException;
            return Task.FromResult( IdentityResult.Success );
        }

        /// <summary>
        /// Minimal no-op <see cref="IUserStore{TUser}"/> supplied to the base
        /// <see cref="UserManager{TUser}"/> so it can be constructed; only the Create and FindByEmail
        /// paths are exercised in these tests, and every operation returns success or a default value
        /// without touching a real store.
        /// </summary>
        private sealed class NoOpUserStore : IUserStore<ApplicationUser> {
            /// <summary>Reports a successful create without persisting anything.</summary>
            /// <param name="user">The user to create (unused).</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A successful <see cref="IdentityResult"/>.</returns>
            public Task<IdentityResult> CreateAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );

            /// <summary>Reports a successful delete without persisting anything.</summary>
            /// <param name="user">The user to delete (unused).</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A successful <see cref="IdentityResult"/>.</returns>
            public Task<IdentityResult> DeleteAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );

            /// <summary>No-op; the store holds no disposable resources.</summary>
            public void Dispose( ) { }

            /// <summary>Always reports no user found for the given id.</summary>
            /// <param name="userId">The user id to look up (unused).</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A <see langword="null"/> result.</returns>
            public Task<ApplicationUser?> FindByIdAsync( string userId, CancellationToken ct )
                => Task.FromResult<ApplicationUser?>( null );

            /// <summary>Always reports no user found for the given normalized name.</summary>
            /// <param name="normalizedUserName">The normalized user name to look up (unused).</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A <see langword="null"/> result.</returns>
            public Task<ApplicationUser?> FindByNameAsync( string normalizedUserName, CancellationToken ct )
                => Task.FromResult<ApplicationUser?>( null );

            /// <summary>Returns the user's current normalized user name.</summary>
            /// <param name="user">The user to read.</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>The user's normalized user name.</returns>
            public Task<string?> GetNormalizedUserNameAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.NormalizedUserName );

            /// <summary>Returns the user's id, or an empty string when unset.</summary>
            /// <param name="user">The user to read.</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>The user's id, or <see cref="string.Empty"/> when null.</returns>
            public Task<string> GetUserIdAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.Id ?? string.Empty );

            /// <summary>Returns the user's current user name.</summary>
            /// <param name="user">The user to read.</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>The user's user name.</returns>
            public Task<string?> GetUserNameAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( user.UserName );

            /// <summary>Stores the supplied normalized user name on the in-memory user instance.</summary>
            /// <param name="user">The user to mutate.</param>
            /// <param name="normalizedName">The normalized name to assign.</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A completed task.</returns>
            public Task SetNormalizedUserNameAsync( ApplicationUser user, string? normalizedName, CancellationToken ct ) {
                user.NormalizedUserName = normalizedName;
                return Task.CompletedTask;
            }

            /// <summary>Stores the supplied user name on the in-memory user instance.</summary>
            /// <param name="user">The user to mutate.</param>
            /// <param name="userName">The user name to assign.</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A completed task.</returns>
            public Task SetUserNameAsync( ApplicationUser user, string? userName, CancellationToken ct ) {
                user.UserName = userName;
                return Task.CompletedTask;
            }

            /// <summary>Reports a successful update without persisting anything.</summary>
            /// <param name="user">The user to update (unused).</param>
            /// <param name="ct">A cancellation token (unused).</param>
            /// <returns>A successful <see cref="IdentityResult"/>.</returns>
            public Task<IdentityResult> UpdateAsync( ApplicationUser user, CancellationToken ct )
                => Task.FromResult( IdentityResult.Success );
        }
    }
}
