# Testing

BridgeBeats has a single test project, `Tests/BridgeBeats.Tests.csproj`, that
holds all unit, integration, and end-to-end tests. This page covers how the
tests are organized, how to run each category locally, and how the CI test job
runs them.

For contribution rules around when to add tests, see the
[Testing Guidelines](https://github.com/tsmarvin/BridgeBeats/blob/develop/.github/CONTRIBUTING.md#testing-guidelines) in the
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
requires a running Docker engine. The shared container is set up once in
`SharedTestInfrastructure`. When Docker is not available, that setup catches the
`DockerUnavailableException`, and each Redis-dependent test calls
`Assert.Inconclusive` — it is reported as not-run rather than failed.

The CI test job guards against an entire integration lane silently going
not-run: after the integration step it reads the result `.trx` file and fails
the workflow if zero integration tests executed. That guard catches the case
where the *whole* integration lane runs nothing — but it does not, on its own,
prove that the Docker-backed Redis tests ran (see
[What CI does and does not do today](#what-ci-does-and-does-not-do-today)).

## How CI runs the tests

The `Tests` workflow (`.github/workflows/tests.yml`) runs on pushes and pull
requests targeting `main` and `develop`. It runs each category as a separate
step, in order, against a Release build:

1. Restore dependencies (with lock-file validation) and build
   (`--configuration Release`).
2. Write a test `appsettings.json` from `appsettings.transform.json`, injecting
   provider credentials from CI secrets and leaving the Discord token empty so
   the bot stays disabled during tests.
3. Run unit tests (`--filter "FullyQualifiedName~Unit"`).
4. Run integration tests (`--filter "FullyQualifiedName~Integration"`).
5. Verify at least one integration test executed (the not-run guard above),
   parsed from the integration `.trx` file's `executed` count.
6. Run end-to-end tests (`--filter "FullyQualifiedName~EndToEnd"`).

Results are written as `.trx` files and uploaded as a build artifact (retained
for 30 days).

## What CI does and does not do today

This section reflects the current workflow, not the target state.

**What CI does:**

- Builds in Release and runs the unit, integration, and end-to-end lanes in
  order.
- Fails the build if the integration lane executed zero tests (the not-run
  guard).
- Injects provider credentials from secrets so credentialed paths can run.

**What CI does not do yet:**

- **CI does not provision Docker.** The runner has no Docker engine available to
  the test step, so the Testcontainers-backed Redis tests hit
  `DockerUnavailableException` and call `Assert.Inconclusive`. Those specific
  tests are reported as not-run, and the integration lane can still report green
  as long as at least one non-Docker integration test executed. The not-run
  guard checks the lane is not entirely empty; it does not assert that the
  Docker-dependent tests actually ran.

The practical effect: a green integration result in CI today does not guarantee
the Redis-backed integration tests ran their assertions.

### Gap against the planned CI quality-gate policy

The project's planned CI quality-gate policy (see the Appendix of
`projects/NEXT-STEPS.md`) is a 7-item policy intended to apply once the related
test work lands. The current workflow does not yet meet it. The two largest
gaps, both of which require CI to provision Docker:

- **Item 1 — treat Inconclusive as a gate failure** for the Integration, Docker,
  and EndToEnd categories, with CI provisioning Docker. Today, Inconclusive is
  treated as not-run, not as a failure.
- **Item 2 — move live-API provider tests to a Manual-only category** excluded
  from the green gate, with fixture-based unit coverage standing in for them in
  CI. Today there is no Manual-only category, so live-network tests that degrade
  to inconclusive or early-return can pass without asserting.

These are tracked as post-merge work in `projects/NEXT-STEPS.md` (Section 6 and
the Appendix); this guide documents the current behavior so contributors do not
assume the gate is already enforced.

## Writing tests

- Use MSTest attributes: `[TestClass]`, `[TestMethod]`, `[TestInitialize]`,
  `[TestCleanup]`.
- Use Moq for mocking dependencies.
- Use `WebApplicationFactory` for web endpoint tests.
- For Redis-backed tests, use `SharedTestInfrastructure` and guard on Docker
  availability so the test is skipped (not failed) when Docker is absent.
- Cover both success and failure paths.
- Name a test for the behavior it checks.

Place the test in the folder and namespace that match its category so the CI
filters pick it up.
