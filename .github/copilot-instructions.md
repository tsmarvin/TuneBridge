# Copilot Instructions for TuneBridge

## Project Overview

**TuneBridge** is a cross-platform music link converter and lookup service that bridges music providers (such as Apple Music and Spotify). The application provides:
- Web-based lookup interface for music link conversion
- RESTful API endpoints for programmatic access
- Discord bot integration for automatic link conversion in chat messages
- Accurate matching using ISRC (tracks) and UPC (albums) identifiers
- Fuzzy metadata matching fallback when external IDs are unavailable

**Primary Goals:**
- Seamless music sharing across streaming platforms
- High accuracy in track/album matching
- Easy deployment via Docker containers
- Robust API integrations with third-party services

## Technology Stack

### Core Framework
- **.NET 9.0** - Primary framework (targeting `net9.0`)
- **ASP.NET Core** - Web framework with MVC pattern
- **C#** with nullable reference types enabled

### Key Libraries
- **NetCord** - Discord bot library for gateway events
- **NetCord.Hosting** - Discord bot hosting integration
- **Microsoft.Extensions.Http.Resilience** - HTTP resilience with retry policies
- **Microsoft.IdentityModel.JsonWebTokens** - JWT token handling for Apple Music API

### Testing Framework
- **MSTest** - Primary testing framework
- **Moq** - Mocking framework
- **FluentAssertions** - Assertion library
- **Microsoft.AspNetCore.Mvc.Testing** - Integration testing for web endpoints

### External APIs
- **Apple MusicKit API** - Apple Music integration (requires Team ID, Key ID, and .p8 private key)
- **Spotify Web API** - Spotify integration (requires Client ID and Client Secret)
- **Discord API** - Bot integration (requires Discord bot token)

## Repository Structure

```
TuneBridge/
├── Configuration/          # Application settings and startup configuration
│   ├── AppSettings.cs      # Service credentials and configuration model
│   ├── DiscordNodeConfig.cs # Discord sharding configuration
│   └── StartupExtensions.cs # Dependency injection and service registration
├── Domain/                 # Core business logic
│   ├── Contracts/          # Data transfer objects and contracts
│   ├── Implementations/    # Service implementations
│   │   ├── Auth/          # Authentication handlers (Apple JWT, Spotify tokens)
│   │   ├── Services/      # Music lookup services
│   │   └── DiscordGatewayHandlers/ # Discord message handlers
│   ├── Interfaces/         # Service abstractions
│   └── Types/             # Domain types and enums
├── Web/                    # ASP.NET Core web application
│   ├── Controllers/        # MVC controllers
│   ├── Models/            # View models
│   └── Views/             # Razor views
├── TuneBridge.Tests/      # Test project
│   ├── Unit/              # Unit tests
│   ├── Integration/       # Integration tests
│   └── EndToEnd/          # End-to-end tests
├── docs/                  # Documentation (DocFX)
└── Program.cs             # Application entry point
```

## Coding Standards and Conventions

### Naming Conventions
- **PascalCase**: Classes, methods, properties, constants, public fields, namespaces
- **camelCase with underscore prefix**: Instance fields (e.g., `_fieldName`)
- **camelCase with s_ prefix**: Static fields (e.g., `s_staticField`)
- **camelCase**: Local variables, parameters, local functions parameters

### Code Style (from .editorconfig)
- **Indentation**: 4 spaces for C# files, 2 spaces for JSON/XML/Shell scripts
- **Braces**: Egyptian style - opening braces on same line (K&R style)
- **Line endings**: CRLF for C# files, LF for shell scripts
- **Encoding**: UTF-8 with BOM for C# files
- **Prefer explicit types** over `var` (except when type is apparent)
- **Use language keywords** instead of framework type names (e.g., `string` not `String`)
- **Prefer modern C# features**: pattern matching, null propagation, expression-bodied members
- **Method parameter parentheses**: Always include spaces (e.g., `Method( param )`)
- **Namespace declarations**: Use block-scoped namespaces

### Architectural Patterns
- **Dependency Injection**: All services registered in `StartupExtensions.cs`
- **Resilience patterns**: HTTP clients use standard resilience with exponential backoff
  - Max 5 retry attempts
  - 1-30 second delay range with jitter
  - Honor `Retry-After` headers
  - 20-second total timeout, 10-second per-attempt timeout
  - Retry disabled for unsafe HTTP methods (POST/PUT/PATCH/DELETE)
- **Fail-fast validation**: Configuration validation at startup (missing credentials, invalid files)
- **Optional service registration**: Services only registered if credentials are present

### Security Guidelines
- **Never commit secrets** - All credentials via environment variables
- **Required environment variables**: Validated in `entrypoint.sh` for Docker
- **Private key handling**: .p8 files validated for existence and non-empty content
- **API authentication**: JWT for Apple Music, OAuth2 client credentials for Spotify

## Configuration and Environment

### Required Environment Variables
- `APPLE_TEAM_ID` - Apple Developer Team ID
- `APPLE_KEY_ID` - Apple Music API Key ID
- `APPLE_KEY_PATH` - Path to .p8 private key file
- `SPOTIFY_CLIENT_ID` - Spotify API Client ID
- `SPOTIFY_CLIENT_SECRET` - Spotify API Client Secret
- `DISCORD_TOKEN` - Discord bot token

### Optional Environment Variables
- `NODE_NUMBER` - Discord shard node number (default: 0)
- `ALLOWED_HOSTS` - Web server allowed hosts (default: *)
- `DEFAULT_LOGLEVEL` - Logging level (default: Information)
- `HOSTING_DEFAULT_LOGLEVEL` - ASP.NET hosting log level (default: Information)

### Application Settings
- Configuration loaded from `appsettings.json` (or generated by `entrypoint.sh` in Docker)
- Settings model: `TuneBridge.Configuration.AppSettings`
- Kestrel listens on port 10000 (HTTP) for container deployments

## Testing Guidelines

### Test Organization
- **Unit tests**: `TuneBridge.Tests/Unit/` - Test individual components in isolation
- **Integration tests**: `TuneBridge.Tests/Integration/` - Test service interactions
- **End-to-end tests**: `TuneBridge.Tests/EndToEnd/` - Test full request/response flows

### Testing Practices
- Use **MSTest** attributes: `[TestClass]`, `[TestMethod]`, `[TestInitialize]`, `[TestCleanup]`
- Use **Moq** for mocking dependencies
- Use **FluentAssertions** for readable assertions (e.g., `result.Should().NotBeNull()`)
- Use **WebApplicationFactory** for integration testing web endpoints
- Test both success and failure scenarios
- Validate configuration at startup to catch issues early

### Running Tests
```bash
dotnet test
```

## API Integration Patterns

### Apple Music API
- Base URL: `https://api.music.apple.com/v1/catalog/`
- Authentication: JWT bearer token (generated via `AppleJwtHandler`)
- Token expiration: 6 months (handled automatically)
- HTTP client registered as `"musickit-api"` with resilience

### Spotify API
- Authentication: OAuth2 client credentials flow
- Token refresh: Automatic via `SpotifyTokenHandler`
- HTTP client with standard resilience patterns

### Discord Bot
- Uses NetCord library for gateway events
- Handles `MessageCreate` events to detect music links
- Sharding support via `NODE_NUMBER` configuration
- Graceful degradation if Discord token not provided

## Important Resources

### External Documentation
- [Apple MusicKit API Documentation](https://developer.apple.com/documentation/applemusicapi)
- [Apple Developer - Create Media Identifier](https://developer.apple.com/help/account/configure-app-capabilities/create-a-media-identifier-and-private-key/)
- [Spotify Web API Documentation](https://developer.spotify.com/documentation/web-api)
- [Spotify - Register Your App](https://developer.spotify.com/documentation/general/guides/app-settings/#register-your-app)
- [Discord Developer Documentation](https://discord.com/developers/docs)
- [Discord - Getting Started](https://discord.com/developers/docs/quick-start/getting-started)
- [NetCord Library](https://github.com/NetCordDev/NetCord)

### Build and Deployment
- Docker support with multi-stage builds
- Entry point script: `entrypoint.sh` (validates env vars and generates config)
- Published as self-contained single-file executable
- Supports: win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64

## Communication Guidelines

- Do NOT create documentation files (e.g., SECURITY_UPDATE.md, CHANGELOG.md, etc.) to communicate changes or updates
- Communicate all updates, explanations, and information through PR comments instead
- Keep documentation minimal and only create files when specifically requested by the repository owner

## Development Workflow

### Building
```bash
dotnet build
```

### Running Locally
```bash
dotnet run
```

### Docker Build
```bash
docker build -t tunebridge .
```

### Common Commands
- Restore dependencies: `dotnet restore`
- Run tests: `dotnet test`
- Format code: Follow .editorconfig rules (enforced by IDE)
- Check code analysis: Build warnings include analyzer diagnostics
