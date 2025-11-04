# TuneBridge Test Suite

This directory contains comprehensive unit, integration, and end-to-end tests for the TuneBridge application.

## Test Organization

The test suite is organized into three categories:

### Unit Tests (`/Tests/Unit/`)
Tests individual components in isolation without external dependencies.
- **AppSettingsTests** - Configuration binding and validation
- **AppleJwtHandlerTests** - JWT token generation for Apple MusicKit API
- **ConfigurationValidationTests** - Startup configuration validation and error handling

**All unit tests pass locally without any external dependencies or secrets.**

### Integration Tests (`/Tests/Integration/`)
Tests service integrations with real API calls to third-party services.
- **MusicLookupServiceTests** - Tests actual API calls to Spotify, Apple Music, and Tidal

**Integration tests require valid API credentials and will fail locally without them.**

### End-to-End Tests (`/Tests/EndToEnd/`)
Tests the full request/response cycle through the web application.
- **HomeControllerTests** - Web interface endpoints
- **MusicLookupControllerTests** - REST API endpoints for music lookup

**Most end-to-end tests require valid API credentials and will fail locally without them.**

## Running Tests Locally

### Prerequisites
- .NET 9.0 SDK installed
- Valid API credentials (optional, for integration/E2E tests)

### Running All Tests
```bash
dotnet test
```

### Running Specific Test Categories
```bash
# Unit tests only (no credentials required)
dotnet test --filter "FullyQualifiedName~Unit"

# Integration tests only (requires credentials)
dotnet test --filter "FullyQualifiedName~Integration"

# End-to-end tests only (requires credentials)
dotnet test --filter "FullyQualifiedName~EndToEnd"
```

### Running with Verbose Output
```bash
dotnet test --verbosity detailed
```

## Configuration for Local Testing

### Unit Tests
No configuration required - all unit tests work out of the box.

### Integration and End-to-End Tests

To run integration and end-to-end tests locally, you need to provide valid API credentials in the `Tests/appsettings.json` file:

```json
{
  "TuneBridge": {
    "AppleTeamId": "YOUR_APPLE_TEAM_ID",
    "AppleKeyId": "YOUR_APPLE_KEY_ID",
    "AppleKeyPath": "/path/to/your/apple_key.p8",
    "SpotifyClientId": "YOUR_SPOTIFY_CLIENT_ID",
    "SpotifyClientSecret": "YOUR_SPOTIFY_CLIENT_SECRET",
    "TidalClientId": "YOUR_TIDAL_CLIENT_ID",
    "TidalClientSecret": "YOUR_TIDAL_CLIENT_SECRET",
    "ConnectionString": "Data Source=Identity;Mode=Memory;Cache=Shared",
    "ApiKeySalt": "your_api_key_salt",
    "CacheDbPath": "Data Source=LinkCache;Mode=Memory;Cache=Shared"
  }
}
```

**Important**: Never commit real API credentials to the repository. The `Tests/appsettings.json` file should contain empty strings for sensitive values when committed.

## Running Tests in CI/CD

Tests are automatically run in GitHub Actions on push and pull requests. The CI workflow uses GitHub secrets to provide valid API credentials.

### Required GitHub Secrets
The following secrets must be configured in the GitHub repository settings:

- `APPLETEAMID` - Apple Developer Team ID
- `APPLEKEYID` - Apple Music API Key ID
- `APPLEPRIVATEKEY` - Apple Music API private key (contents of .p8 file)
- `SPOTIFYCLIENTID` - Spotify API Client ID
- `SPOTIFYCLIENTSECRET` - Spotify API Client Secret
- `TIDALCLIENTID` - Tidal API Client ID
- `TIDALCLIENTSECRET` - Tidal API Client Secret
- `APIKEYSALT` - Salt for API key hashing

### Optional GitHub Variables
- `NODENUMBER` - Discord shard node number (defaults to 0)

### How CI Configuration Works

1. The CI workflow (`../.github/workflows/tests.yml`) runs after building the project
2. It writes the Apple private key from secrets to a temporary file (`/tmp/test-keys/apple_key.p8`)
3. It generates `Tests/bin/Release/net9.0/appsettings.json` from the template using `sed` to replace placeholders with secret values
4. Tests run with these credentials, making real API calls to verify functionality

### CI Test Execution

The CI workflow runs tests in three stages:

1. **Unit Tests**: `dotnet test --filter "FullyQualifiedName~Unit"`
2. **Integration Tests**: `dotnet test --filter "FullyQualifiedName~Integration"`
3. **End-to-End Tests**: `dotnet test --filter "FullyQualifiedName~EndToEnd"`

Test results are uploaded as artifacts for 30 days.

## Test Coverage

### Current Test Coverage
- **Unit Tests**: 12 tests covering configuration, JWT token generation, and validation
- **Integration Tests**: 8 tests covering real API interactions with music providers
- **End-to-End Tests**: 13 tests covering web UI and REST API endpoints

### Expected Test Results

#### Without Valid Credentials (Local Development)
- ✅ Unit Tests: 12/12 passing
- ❌ Integration Tests: 0/8 passing (requires API credentials)
- ❌ End-to-End Tests: 6/13 passing (some endpoints don't require credentials)

#### With Valid Credentials (CI/CD)
- ✅ Unit Tests: 12/12 passing
- ✅ Integration Tests: 8/8 passing
- ✅ End-to-End Tests: 13/13 passing

## Adding New Tests

When adding new tests, follow these guidelines:

1. **Use appropriate test attributes**:
   - `[TestClass]` for test classes
   - `[TestMethod]` for test methods
   - `[TestInitialize]` for setup
   - `[TestCleanup]` for teardown

2. **Use FluentAssertions** for readable assertions:
   ```csharp
   result.Should().NotBeNull();
   result.Count.Should().BeGreaterThan(0);
   ```

3. **Test both success and failure scenarios**

4. **Name tests descriptively**:
   - Use the pattern: `MethodName_Scenario_ExpectedBehavior`
   - Example: `GetInfoByISRC_WithValidISRC_ShouldReturnResult`

5. **Add XML documentation comments** explaining what the test validates

6. **Keep tests independent** - each test should be able to run in isolation

## Troubleshooting

### Tests fail with "Required settings are missing"
- Unit tests: Check that `Tests/appsettings.json` has valid JSON structure
- Integration/E2E tests: Provide valid API credentials in `Tests/appsettings.json`

### Tests fail with "ApiKeySalt is required"
- Add a test value for `ApiKeySalt` in your `Tests/appsettings.json` configuration

### Apple Music tests fail with file not found
- Ensure the Apple private key file exists at the path specified in `AppleKeyPath`
- The file should be a valid .p8 file with PEM-formatted private key

### Spotify/Tidal tests fail with authentication errors
- Verify your Client ID and Client Secret are correct
- Ensure there are no extra spaces or quotes in the credentials

## Resources

- [MSTest Documentation](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-mstest)
- [FluentAssertions Documentation](https://fluentassertions.com/introduction)
- [ASP.NET Core Integration Tests](https://docs.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [Apple MusicKit API](https://developer.apple.com/documentation/applemusicapi)
- [Spotify Web API](https://developer.spotify.com/documentation/web-api)
