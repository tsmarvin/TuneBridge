# Testing Guide

TuneBridge includes a comprehensive test suite with unit, integration, and end-to-end tests. This guide covers how to run and write tests.

## Test Organization

Tests are organized in the `/Tests` directory:

- **Unit Tests** (`Unit/`) - Test individual components in isolation
- **Integration Tests** (`Integration/`) - Test service integration with external APIs  
- **End-to-End Tests** (`EndToEnd/`) - Test complete application flows including API endpoints

## Running Tests

### All Tests

```bash
dotnet test
```

### Unit Tests Only

Unit tests don't require API credentials and can run in any environment:

```bash
dotnet test --filter "FullyQualifiedName~Unit"
```

### Integration Tests

Integration tests make real API calls and require valid credentials:

```bash
dotnet test --filter "FullyQualifiedName~Integration"
```

### End-to-End Tests

E2E tests exercise the full application stack:

```bash
dotnet test --filter "FullyQualifiedName~EndToEnd"
```

### Specific Test Class

```bash
dotnet test --filter "FullyQualifiedName~AppleMusicLookupServiceTests"
```

### Specific Test Method

```bash
dotnet test --filter "FullyQualifiedName~AppleMusicLookupServiceTests.GetInfoByISRCAsync_WithValidISRC_ReturnsResult"
```

## Test Configuration

### appsettings.json

Tests read credentials from `appsettings.json` in the test output directory. Create this file with your credentials:

```json
{
  "TuneBridge": {
    "NodeNumber": 0,
    "AppleTeamId": "your_team_id",
    "AppleKeyId": "your_key_id",
    "AppleKeyPath": "/path/to/AuthKey.p8",
    "SpotifyClientId": "your_client_id",
    "SpotifyClientSecret": "your_client_secret",
    "TidalClientId": "your_tidal_client_id",
    "TidalClientSecret": "your_tidal_client_secret",
    "DiscordToken": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.Hosting.Lifetime": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

### CI/CD Configuration

In CI pipelines, use the `appsettings.transform.json` template with GitHub secrets:

```bash
# Example GitHub Actions workflow
- name: Create appsettings.json
  run: |
    cat > appsettings.json << EOF
    {
      "TuneBridge": {
        "AppleTeamId": "${{ secrets.APPLE_TEAM_ID }}",
        "AppleKeyId": "${{ secrets.APPLE_KEY_ID }}",
        "AppleKeyPath": "${{ secrets.APPLE_KEY_PATH }}",
        "SpotifyClientId": "${{ secrets.SPOTIFY_CLIENT_ID }}",
        "SpotifyClientSecret": "${{ secrets.SPOTIFY_CLIENT_SECRET }}"
      }
    }
    EOF
```

## Writing Tests

### Unit Test Example

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;

namespace TuneBridge.Tests.Unit {
    [TestClass]
    public class MyServiceTests {
        private MyService _service;

        [TestInitialize]
        public void Setup() {
            // Arrange - create test dependencies
            _service = new MyService();
        }

        [TestMethod]
        public async Task MethodName_Scenario_ExpectedBehavior() {
            // Arrange
            string input = "test";

            // Act
            var result = await _service.ProcessAsync(input);

            // Assert
            result.Should().NotBeNull();
            result.Value.Should().Be("expected");
        }

        [TestCleanup]
        public void Cleanup() {
            // Clean up resources
            _service?.Dispose();
        }
    }
}
```

### Integration Test Example

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace TuneBridge.Tests.Integration {
    [TestClass]
    public class AppleMusicServiceIntegrationTests {
        private static IConfiguration _configuration;
        private AppleMusicLookupService _service;

        [ClassInitialize]
        public static void ClassSetup(TestContext context) {
            // Load configuration once for all tests
            _configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
        }

        [TestInitialize]
        public void Setup() {
            // Create service with real HTTP client
            _service = new AppleMusicLookupService(
                _configuration["TuneBridge:AppleTeamId"],
                _configuration["TuneBridge:AppleKeyId"],
                _configuration["TuneBridge:AppleKeyPath"]
            );
        }

        [TestMethod]
        public async Task GetInfoByISRCAsync_WithRealAPI_ReturnsResult() {
            // Arrange
            string isrc = "USVI20400123"; // Known valid ISRC

            // Act
            var result = await _service.GetInfoByISRCAsync(isrc);

            // Assert
            result.Should().NotBeNull();
            result.ExternalId.Should().Be(isrc);
        }
    }
}
```

### End-to-End Test Example

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;
using System.Net.Http.Json;

namespace TuneBridge.Tests.EndToEnd {
    [TestClass]
    public class MusicLookupApiTests {
        private static WebApplicationFactory<Program> _factory;
        private static HttpClient _client;

        [ClassInitialize]
        public static void ClassSetup(TestContext context) {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }

        [TestMethod]
        public async Task POST_LookupUrl_ReturnsValidResult() {
            // Arrange
            var request = new { uri = "https://music.apple.com/us/album/..." };

            // Act
            var response = await _client.PostAsJsonAsync("/music/lookup/url", request);

            // Assert
            response.Should().BeSuccessful();
            var result = await response.Content.ReadFromJsonAsync<MediaLinkResult>();
            result.Should().NotBeNull();
            result.Results.Should().NotBeEmpty();
        }

        [ClassCleanup]
        public static void ClassCleanup() {
            _client?.Dispose();
            _factory?.Dispose();
        }
    }
}
```

## Testing Best Practices

### 1. Follow AAA Pattern

Structure tests with Arrange, Act, Assert:

```csharp
[TestMethod]
public async Task TestMethod() {
    // Arrange - Set up test data and dependencies
    var input = "test";
    
    // Act - Execute the code being tested
    var result = await _service.ProcessAsync(input);
    
    // Assert - Verify the result
    result.Should().NotBeNull();
}
```

### 2. Use Descriptive Test Names

Follow the pattern: `MethodName_Scenario_ExpectedBehavior`

```csharp
[TestMethod]
public async Task GetInfoByISRCAsync_WithValidISRC_ReturnsResult()

[TestMethod]
public async Task GetInfoByISRCAsync_WithInvalidISRC_ReturnsNull()

[TestMethod]
public async Task GetInfoByISRCAsync_WithNullISRC_ThrowsArgumentNullException()
```

### 3. Test Both Success and Failure Paths

```csharp
[TestMethod]
public async Task Success_Path_Test() {
    // Test the happy path
}

[TestMethod]
public async Task Failure_Path_Test() {
    // Test error conditions
}

[TestMethod]
[ExpectedException(typeof(ArgumentException))]
public async Task Invalid_Input_Throws_Exception() {
    // Test exception handling
}
```

### 4. Use FluentAssertions for Readable Tests

```csharp
// Instead of:
Assert.IsNotNull(result);
Assert.AreEqual("expected", result.Value);

// Use:
result.Should().NotBeNull();
result.Value.Should().Be("expected");
```

### 5. Mock External Dependencies in Unit Tests

```csharp
using Moq;

var mockHttpClient = new Mock<IHttpClient>();
mockHttpClient
    .Setup(x => x.GetAsync(It.IsAny<string>()))
    .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });

var service = new MyService(mockHttpClient.Object);
```

### 6. Use Test Fixtures for Shared Setup

```csharp
[TestClass]
public class MyTests {
    private static TestFixture _fixture;

    [ClassInitialize]
    public static void ClassSetup(TestContext context) {
        _fixture = new TestFixture();
    }

    [ClassCleanup]
    public static void ClassCleanup() {
        _fixture?.Dispose();
    }
}
```

## Test Categories

Use test categories to organize and filter tests:

```csharp
[TestClass]
[TestCategory("Integration")]
public class IntegrationTests {
    // ...
}

[TestMethod]
[TestCategory("Slow")]
public async Task LongRunningTest() {
    // ...
}
```

Run tests by category:

```bash
dotnet test --filter "TestCategory=Integration"
dotnet test --filter "TestCategory!=Slow"
```

## Code Coverage

Generate code coverage reports:

```bash
dotnet test --collect:"XPlat Code Coverage"
```

Install ReportGenerator to view coverage:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool

reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coveragereport" \
  -reporttypes:Html

# Open coveragereport/index.html in browser
```

## Continuous Integration

Tests run automatically in CI/CD pipelines when:
- Pull requests are created
- Code is pushed to main branch
- Releases are created

### GitHub Actions Example

```yaml
name: Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: 9.0.x
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Run tests
        run: dotnet test --no-build --verbosity normal
```

## Debugging Tests

### Visual Studio

1. Set breakpoints in test code
2. Right-click test method
3. Select "Debug Test(s)"

### VS Code

1. Install C# extension
2. Set breakpoints
3. Use "Debug Test" code lens or Test Explorer

### Command Line

```bash
# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run tests and wait for debugger to attach
dotnet test --filter "FullyQualifiedName~MyTest" -- RunConfiguration.DebuggerWaitForAttach=true
```

## Performance Testing

For performance-critical code, use BenchmarkDotNet:

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

[MemoryDiagnoser]
public class MyBenchmarks {
    [Benchmark]
    public void TestMethod() {
        // Code to benchmark
    }
}

// Run: dotnet run -c Release
```

## Test Data Management

### Use Constants for Known Data

```csharp
public static class TestData {
    public const string ValidISRC = "USVI20400123";
    public const string ValidSpotifyUrl = "https://open.spotify.com/track/...";
    
    public static MediaLinkResult CreateTestResult() {
        return new MediaLinkResult {
            // ...
        };
    }
}
```

### Use Test Builders

```csharp
public class MediaLinkResultBuilder {
    private MediaLinkResult _result = new();

    public MediaLinkResultBuilder WithProvider(SupportedProviders provider) {
        _result.Results[provider] = new MusicLookupResultDto();
        return this;
    }

    public MediaLinkResult Build() => _result;
}

// Usage:
var result = new MediaLinkResultBuilder()
    .WithProvider(SupportedProviders.Spotify)
    .Build();
```

## Troubleshooting

### Tests fail with "Configuration not found"

Ensure `appsettings.json` exists in the test output directory.

### Integration tests fail with "Unauthorized"

Verify API credentials in `appsettings.json` are valid and up-to-date.

### Tests pass locally but fail in CI

Check that:
- CI has access to required secrets
- File paths are absolute or relative to test output directory
- Line endings are consistent (LF vs CRLF)

### Flaky tests

- Increase timeouts for API calls
- Add retry logic for transient failures
- Use more stable test data
- Avoid time-dependent assertions
