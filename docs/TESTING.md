# Testing

BridgeBeats has a single test project, `Tests/BridgeBeats.Tests.csproj`, that
holds all unit, integration, and end-to-end tests. This page covers how the
tests are organized, how to run each category locally, and how the CI test job
runs them.

For contribution rules around when to add tests, see the
[Testing Guidelines](../.github/CONTRIBUTING.md#testing-guidelines) in the
contributing guide.

## Test stack

The test project targets .NET 10 and uses:

- **MSTest** (`MSTest.TestFramework`, `MSTest.TestAdapter`) for the test runner
  and assertions.
- **Moq** for mocking dependencies.
- **Microsoft.AspNetCore.Mvc.Testing** (`WebApplicationFactory`) for in-process
  web endpoint tests.
- **Testcontainers.Redis** for integration tests that need a real Redis
  instance.
- **Aspire.Hosting.Testing** for distributed application tests that boot the
  AppHost.

These package references are declared in `Tests/BridgeBeats.Tests.csproj`.

## Test organization

Tests are grouped by category, both by folder and by namespace. The namespace is
what test filters match against.

| Category | Folder | Namespace | Purpose |
|---|---|---|---|
| Unit | `Tests/Unit/` | `BridgeBeats.Tests.Unit` | Components in isolation; no external services |
| Integration | `Tests/Integration/` | `BridgeBeats.Tests.Integration` | Service interactions; may need Redis via Testcontainers |
| End-to-end | `Tests/EndToEnd/` | `BridgeBeats.Tests.EndToEnd` | Full request/response flows |

Because the category is part of the fully qualified test name, the
`FullyQualifiedName~<Category>` filter selects a single lane.

## Running tests locally

Build once, then run the category you want.

```bash
# Build first
dotnet build

# Run everything
dotnet test

# Run a single category
dotnet test --filter "FullyQualifiedName~Unit"
dotnet test --filter "FullyQualifiedName~Integration"
dotnet test --filter "FullyQualifiedName~EndToEnd"

# Add detailed console output
dotnet test --logger "console;verbosity=detailed"
```

Unit tests need no API credentials or external services. Integration and
end-to-end tests may need Docker running (see below) and, depending on the test,
provider credentials in `src/BridgeBeats.Web/appsettings.json`.

## Integration tests and Docker

Integration tests that need Redis start a container through Testcontainers, which
requires a running Docker engine. When Docker is not available, those tests call
`Assert.Inconclusive` and are reported as not-run rather than failed.

The CI test job guards against an entire integration lane silently going
not-run: after the integration step it reads the result `.trx` file and fails
the workflow if zero integration tests executed. A new all-inconclusive test
class therefore cannot pass the integration lane unnoticed.

## How CI runs the tests

The `Tests` workflow (`.github/workflows/tests.yml`) runs on pushes and pull
requests targeting `main` and `develop`. It runs each category as a separate
step, in order, against a Release build:

1. Restore and build (`--configuration Release`).
2. Run unit tests (`--filter "FullyQualifiedName~Unit"`).
3. Run integration tests (`--filter "FullyQualifiedName~Integration"`).
4. Verify at least one integration test executed (the not-run guard above).
5. Run end-to-end tests (`--filter "FullyQualifiedName~EndToEnd"`).

Results are written as `.trx` files and uploaded as a build artifact.

## Writing tests

- Use MSTest attributes: `[TestClass]`, `[TestMethod]`, `[TestInitialize]`,
  `[TestCleanup]`.
- Use Moq for mocking dependencies.
- Use `WebApplicationFactory` for web endpoint tests.
- Cover both success and failure paths.
- Name a test for the behavior it checks.

Place the test in the folder and namespace that match its category so the CI
filters pick it up.
