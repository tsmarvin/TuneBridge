# Testing Guide

This document describes how to run tests for TuneBridge both locally and in CI environments.

## Test Structure

The test suite is organized into three categories:

- **Unit Tests** (`TuneBridge.Tests/Unit/`): Fast, isolated tests for individual components
- **Integration Tests** (`TuneBridge.Tests/Integration/`): Tests that verify service integration with external APIs
- **End-to-End Tests** (`TuneBridge.Tests/EndToEnd/`): Full application tests including API endpoints

## Prerequisites

### Required Software
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Required API Credentials

To run integration and end-to-end tests, you need valid API credentials. See the main [README.md](../README.md) for instructions on obtaining these credentials.

## Configuration

Tests use a separate `appsettings.test.json` file that gets copied to the test output as `appsettings.json`. This allows the main application's `appsettings.json` to maintain its template format for Docker container deployment while tests use a valid JSON file with values populated from environment variables.

The test framework uses environment variables to access API credentials and configuration. The following table shows the mapping between GitHub secrets/variables and environment variables:

| Purpose | GitHub Secret/Variable | Environment Variable | Notes |
|---------|----------------------|---------------------|-------|
| Apple Team ID | `APPLETEAMID` (secret) | `APPLETEAMID` | Required for Apple Music tests |
| Apple Key ID | `APPLEKEYID` (secret) | `APPLEKEYID` | Required for Apple Music tests |
| Apple Private Key | `APPLEPRIVATEKEY` (secret) | `APPLEPRIVATEKEY` | Contents of .p8 file; will be written to temp file |
| Apple Key Path | N/A | `APPLEKEYPATH` | Alternative: Direct path to .p8 file on disk |
| Node Number | `NODENUMBER` (variable) | `NODENUMBER` | Defaults to 0 if not set |
| Spotify Client ID | `SPOTIFYCLIENTID` (secret) | `SPOTIFYCLIENTID` | Required for Spotify tests |
| Spotify Client Secret | `SPOTIFYCLIENTSECRET` (secret) | `SPOTIFYCLIENTSECRET` | Required for Spotify tests |
| Discord Token | `DISCORDTOKEN` (secret) | `DISCORDTOKEN` | Optional for most tests |

### Setting Up for Local Testing

#### Option 1: Using Apple Private Key File
Create a `.env` file or set environment variables directly:

```bash
export APPLETEAMID="your_team_id"
export APPLEKEYID="your_key_id"
export APPLEKEYPATH="/path/to/your/AuthKey.p8"
export SPOTIFYCLIENTID="your_client_id"
export SPOTIFYCLIENTSECRET="your_client_secret"
export NODENUMBER="0"
```

#### Option 2: Using Apple Private Key Contents
Alternatively, you can provide the key contents directly:

```bash
export APPLETEAMID="your_team_id"
export APPLEKEYID="your_key_id"
export APPLEPRIVATEKEY="$(cat /path/to/your/AuthKey.p8)"
export SPOTIFYCLIENTID="your_client_id"
export SPOTIFYCLIENTSECRET="your_client_secret"
export NODENUMBER="0"
```

When `APPLEPRIVATEKEY` is set, the test framework automatically writes it to a temporary file and uses that path.

## Running Tests

### Run All Tests
```bash
dotnet test
```

### Run Unit Tests Only
```bash
dotnet test --filter "FullyQualifiedName~Unit"
```

Unit tests do not require API credentials and will run even without secrets configured.

### Run Integration Tests Only
```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

Integration tests require valid Apple Music and Spotify API credentials.

### Run End-to-End Tests Only
```bash
dotnet test --filter "FullyQualifiedName~EndToEnd"
```

End-to-end tests require valid API credentials and test the full application stack.

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~AppleJwtHandlerTests"
```

### Run with Detailed Output
```bash
dotnet test --logger "console;verbosity=detailed"
```

## Test Behavior Without Secrets

When API credentials are not available:

- **Unit tests**: Run normally (they don't require external APIs)
- **Integration and E2E tests**: Gracefully skip by returning early without failing

This allows the test suite to run in environments where secrets are not available without causing failures.

## CI/CD Configuration

Tests run automatically in GitHub Actions on:
- Push to `main` or `develop` branches
- Pull requests targeting `main` or `develop`

### GitHub Secrets Setup

To enable tests in CI, configure the following secrets in your GitHub repository settings:

1. Go to Settings → Secrets and variables → Actions
2. Add the following **Repository Secrets**:
   - `APPLETEAMID`
   - `APPLEKEYID`
   - `APPLEPRIVATEKEY` (paste the entire contents of your .p8 file)
   - `SPOTIFYCLIENTID`
   - `SPOTIFYCLIENTSECRET`
   - `DISCORDTOKEN` (optional)

3. Add the following **Repository Variable**:
   - `NODENUMBER` (default: 0)

The CI workflow (`.github/workflows/tests.yml`) automatically:
- Writes the Apple private key from the secret to a temporary file
- Sets all required environment variables
- Runs unit, integration, and end-to-end tests separately
- Uploads test results as artifacts
- Cleans up temporary files

## Troubleshooting

### Tests Skip or Pass Without Running
If integration or E2E tests appear to pass but don't actually test anything, check that:
1. All required environment variables are set
2. The Apple private key file exists and is readable (if using `APPLEKEYPATH`)
3. API credentials are valid and not expired

### Apple Key File Errors
If you see errors about missing or invalid Apple key files:
- Verify the .p8 file exists at the path specified in `APPLEKEYPATH`
- Ensure the file contains valid PEM-formatted private key
- Check file permissions (should be readable)

### Integration Test Failures
If integration tests fail with network or API errors:
- Verify your API credentials are valid and not expired
- Check that you have internet connectivity
- Ensure the external APIs (Apple Music, Spotify) are accessible

## Code Coverage

To generate code coverage reports:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Coverage reports will be generated in the `TestResults` directory.

## Writing New Tests

When adding new tests:

1. Place unit tests in `TuneBridge.Tests/Unit/`
2. Place integration tests in `TuneBridge.Tests/Integration/`
3. Place end-to-end tests in `TuneBridge.Tests/EndToEnd/`
4. Use the `TestConfiguration.AreSecretsAvailable()` helper to skip tests that require credentials
5. Follow existing test patterns and naming conventions
6. Ensure tests clean up any resources they create

Example test that gracefully handles missing secrets:

```csharp
[Fact]
public async Task MyTest()
{
    if (!TestConfiguration.AreSecretsAvailable())
    {
        return; // Skip test if secrets not available
    }
    
    // Test implementation...
}
```
