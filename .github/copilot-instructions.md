# Copilot Instructions for TuneBridge

## Repository Overview

TuneBridge is a cross-platform music link converter and lookup service that bridges music provider services (such as Apple Music and Spotify). It provides:
- A web interface for manual music lookups
- RESTful API endpoints for programmatic access
- A Discord bot that automatically detects and converts music links in Discord messages

**Architecture**: ASP.NET Core web application with optional Discord bot integration
- Web/API layer using ASP.NET Core MVC with Razor views
- Domain layer with services, interfaces, and DTOs
- Configuration layer for dependency injection setup
- Integration with Apple MusicKit API and Spotify Web API

**Key Capabilities**:
- Convert music links between Apple Music and Spotify using ISRC (tracks) and UPC (albums)
- Search by URL, external IDs (ISRC/UPC), or title/artist
- Fuzzy matching when external IDs are not available
- Discord bot integration for automatic link conversion in chat

## Technology Stack

**Primary Technologies**:
- **.NET 9.0** - Cross-platform framework (target: `net9.0`)
- **ASP.NET Core** - Web framework for MVC and API
- **C# 13** with nullable reference types enabled

**Key Libraries**:
- **NetCord** - Discord bot gateway and hosting
- **Microsoft.IdentityModel.JsonWebTokens** - JWT authentication for Apple Music API
- **Microsoft.Extensions.Http.Resilience** - HTTP client retry and resilience policies

**External APIs**:
- **Apple MusicKit API** - Requires JWT authentication (Team ID, Key ID, .p8 private key file)
- **Spotify Web API** - Uses OAuth 2.0 client credentials flow

**Deployment**:
- Docker support with multi-stage builds
- Self-contained, single-file publish for cross-platform deployment
- Supported platforms: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-arm64`

## Project Structure

```
TuneBridge/
├── Configuration/          # Dependency injection and startup configuration
│   ├── AppSettings.cs     # Configuration model binding
│   ├── StartupExtensions.cs  # Service registration and HTTP client setup
│   └── DiscordNodeConfig.cs  # Discord sharding configuration
├── Domain/                # Core business logic
│   ├── Contracts/         # Data Transfer Objects (DTOs)
│   │   └── DTOs/
│   ├── Implementations/   # Concrete service implementations
│   │   ├── Auth/         # Authentication handlers (Apple JWT, Spotify OAuth)
│   │   ├── Services/     # Music lookup and link services
│   │   ├── LinkParsers/  # URL parsing for Apple Music and Spotify
│   │   ├── Extensions/   # Helper extensions and Discord message formatting
│   │   └── DiscordGatewayHandlers/  # Discord message event handlers
│   ├── Interfaces/        # Service contracts
│   └── Types/            # Base classes and enums
│       ├── Bases/        # Abstract base classes
│       └── Enums/        # SupportedProviders, SpotifyEntity
├── Web/                  # ASP.NET Core MVC layer
│   ├── Controllers/      # MVC controllers (Home, MusicLookup)
│   ├── Models/          # View models
│   ├── Views/           # Razor views
│   └── wwwroot/         # Static assets (CSS, JS)
├── Program.cs           # Application entry point
├── TuneBridge.csproj    # Project file
└── appsettings.json     # Configuration template
```

## Coding Standards

### Naming Conventions

**C# Naming** (enforced via `.editorconfig`):
- **Types/Classes/Methods/Properties**: `PascalCase`
- **Interfaces**: `IPascalCase` (prefix with `I`)
- **Private instance fields**: `_camelCase` (underscore prefix)
- **Private static fields**: `s_camelCase` (s_ prefix)
- **Parameters/Local variables**: `camelCase`
- **Constants**: `PascalCase`

**File Naming**:
- Match the primary type name: `AppleMusicLookupService.cs`
- Use descriptive names that indicate purpose

### Code Style

**Formatting** (from `.editorconfig`):
- **Indentation**: 4 spaces (no tabs)
- **Line endings**: CRLF for C# files
- **Encoding**: UTF-8 with BOM for C# files
- **Braces**: Same line as declaration (K&R style)
  ```csharp
  public void Method() {
      // code
  }
  ```
- **Spacing**: Spaces in method parameter lists: `Method( param1, param2 )`
- **Expression bodies**: Allowed for all members
- **`var` keyword**: Avoid; prefer explicit types for clarity

**Modern C# Features**:
- Use nullable reference types (`#nullable enable`)
- Prefer pattern matching, null-coalescing, and modern language features
- Use implicit usings (enabled by SDK)
- File-scoped namespaces are allowed but block-scoped is current practice

### XML Documentation

- **All public types and members** must have XML documentation comments (`///`)
- Include `<summary>`, `<param>`, `<returns>`, and `<remarks>` where appropriate
- Add `<exception>` tags for thrown exceptions
- Reference related types using `<see cref=""/>` tags

Example:
```csharp
/// <summary>
/// Looks up track information by ISRC (International Standard Recording Code).
/// </summary>
/// <param name="isrc">The ISRC code of the track.</param>
/// <returns>Music lookup result with metadata, or null if not found.</returns>
Task<MusicLookupResultDto?> GetInfoByISRCAsync( string isrc );
```

## Architectural Patterns

### Dependency Injection

- Services registered in `Configuration/StartupExtensions.cs`
- Use constructor injection for all dependencies
- Register services with appropriate lifetime:
  - `AddSingleton` for stateless services and auth handlers
  - `AddTransient` for lightweight, stateful services
  - `AddScoped` for web request-scoped services

### Service Layer Pattern

**Interfaces** define contracts in `Domain/Interfaces/`:
- `IMusicLookupService` - Provider-specific music API queries
- `IMediaLinkService` - Cross-platform music lookup aggregation

**Base Classes** provide shared logic in `Domain/Types/Bases/`:
- `MusicLookupServiceBase` - Common parsing, sanitization, and validation
- `MediaLinkServiceBase` - Link parsing and result aggregation

**Implementations** in `Domain/Implementations/Services/`:
- `AppleMusicLookupService` - Apple MusicKit API integration
- `SpotifyLookupService` - Spotify Web API integration
- `DefaultMediaLinkService` - Orchestrates multi-provider lookups

### Authentication Handlers

- **Apple Music**: `AppleJwtHandler` generates JWT tokens with ES256 signing
- **Spotify**: `SpotifyTokenHandler` manages OAuth 2.0 client credentials flow with token caching

### HTTP Resilience

All HTTP clients use resilience policies via `AddStandardResilience()`:
- Retry with exponential backoff
- Circuit breaker pattern
- Timeout policies
- Configured in `StartupExtensions.AddStandardResilience()`

### Regular Expressions

Use source-generated regex for performance:
```csharp
[GeneratedRegex(@"pattern", RegexOptions.IgnoreCase)]
private static partial Regex MyRegex();
```

## Key Dependencies

### External APIs

**Apple MusicKit API**:
- Base URL: `https://api.music.apple.com/v1/catalog/`
- Authentication: JWT with ES256 signature
- Required config: `AppleTeamId`, `AppleKeyId`, `AppleKeyPath` (.p8 file)
- Handler: `AppleJwtHandler`

**Spotify Web API**:
- Base URL: `https://api.spotify.com/v1/`
- Authentication: OAuth 2.0 client credentials
- Required config: `SpotifyClientId`, `SpotifyClientSecret`
- Handler: `SpotifyTokenHandler`

### Discord Integration

**NetCord**:
- Gateway intents: `GuildMessages | MessageContent`
- Sharding: Auto-configured by Discord
- Event handlers in `Domain/Implementations/DiscordGatewayHandlers/`
- `MessageCreateGatewayHandler` processes messages and detects music links

### Configuration Binding

Configuration loaded from:
1. `appsettings.json` (template with placeholders)
2. Environment variables
3. Command-line arguments

Bound to `AppSettings` class in `Configuration/`.

## Testing Approach

**Current State**: No test projects exist in the repository

**When adding tests** (future):
- Use MSTest as the testing framework
- Follow AAA pattern: Arrange, Act, Assert
- Name tests descriptively: `MethodName_Scenario_ExpectedBehavior`
- Mock external dependencies (HTTP clients, APIs) using Moq or NSubstitute
- Test base classes and services independently

## Common Workflows

### Adding a New Music Provider

1. Add provider to `SupportedProviders` enum with `[Description]` attribute
2. Create a new service class inheriting from `MusicLookupServiceBase`
3. Implement `IMusicLookupService` interface methods
4. Add HTTP client registration in `StartupExtensions.AddTuneBridgeServices()`
5. Create authentication handler if needed (in `Domain/Implementations/Auth/`)
6. Update provider colors in `AppExtensions.GetPrimaryProviderColor()`

### Adding a New API Endpoint

1. Create/update controller in `Web/Controllers/`
2. Define request/response models (DTOs) if needed
3. Inject required services via constructor
4. Add XML documentation to controller actions
5. Follow existing patterns for async operations and error handling

### Modifying Discord Bot Behavior

1. Update `MessageCreateGatewayHandler` in `Domain/Implementations/DiscordGatewayHandlers/`
2. Use `IMediaLinkService.GetInfoAsync(string content)` for link detection
3. Format responses using `AppExtensions.ToDiscordMessageProperties()`
4. Test with Discord bot in a development server

### Updating Configuration

1. Modify `AppSettings.cs` for new properties
2. Update `appsettings.json` template with placeholders
3. Update README.md configuration section
4. Add validation in `StartupExtensions.AddTuneBridgeServices()` if required

## Domain-Specific Context

### Music Metadata Matching

**External IDs** (preferred method):
- **ISRC** (International Standard Recording Code): Unique identifier for tracks
  - Format: 12 characters (e.g., `USVI20000123`)
  - Most reliable matching method across platforms
- **UPC** (Universal Product Code): Unique identifier for albums
  - Format: 12-13 digit barcode number
  - Best for album matching, especially special editions

**Fuzzy Matching** (fallback when external IDs unavailable):
- Sanitizes titles to remove special characters and normalize formatting
- Handles title variations like "(Remastered)", "(Deluxe Edition)", "- Single"
- Case-insensitive matching
- Artist name matching

**Title Sanitization**:
- Removes parenthetical addendums and brackets
- Normalizes special characters
- Handles regional variations
- Methods: `SanitizeSongTitle()`, `SanitizeAlbumTitle()` in `MusicLookupServiceBase`

### Link Parsing

**URL Patterns**:
- **Apple Music**: `music.apple.com/{storefront}/{type}/{title}/{id}`
  - Types: `album`, `song` (called "music-video" in some URLs)
  - ID extraction via regex in `AppleMusicLinkParser`
- **Spotify**: `open.spotify.com/{type}/{id}`
  - Types: `album`, `track`
  - ID extraction via regex in `SpotifyLinkParser`

### Discord Message Formatting

- Embeds with provider-specific colors (Apple Music: blue, Spotify: green)
- Fields show: Title, Album (for tracks), External IDs (ISRC/UPC)
- Includes album artwork
- Links to both platforms when available
- Original message deleted if it contains only music links (keeps channels clean)

## Communication Guidelines

**CRITICAL - HIGHEST PRIORITY**: 
- **NEVER** create documentation files (e.g., SECURITY_UPDATE.md, CHANGELOG.md, UPDATES.md, etc.) to communicate changes or updates
- **ALWAYS** communicate all updates, explanations, and information through PR comments instead
- Keep documentation minimal and only create files when specifically requested by the repository owner
- This is a strict requirement that must be followed without exception

## Additional Notes

**Performance Considerations**:
- HTTP clients use resilience policies (retry, circuit breaker, timeout)
- Parallel API calls to multiple providers for faster results
- Token caching for Spotify OAuth to reduce auth requests

**Error Handling**:
- Fail fast on missing/invalid configuration (e.g., missing .p8 file)
- For public-facing endpoints: Return `null` from lookup methods when content not found (not exceptions) to prevent crashes
- For internal code: Let exceptions bubble up to a loggable state as quickly as possible
- Log errors and API responses for debugging

**Security**:
- Never commit secrets or API credentials
- Use environment variables for all sensitive configuration
- Private keys (.p8 files) loaded from filesystem, not embedded
- JWT tokens are short-lived and regenerated as needed
