# Copilot Instructions for TuneBridge

## Repository Overview

TuneBridge is a cross-platform music link converter that bridges music provider services (such as Apple Music and Spotify). It provides a web interface, RESTful API, and Discord bot for automatic link conversion using ISRC/UPC-based matching with fuzzy matching fallback.

**Architecture**: ASP.NET Core (.NET 9.0, C# 13) with Domain/Web layers and optional Discord integration (NetCord).

## Project-Specific Conventions

### Code Style
- Use spaces in method parameter lists: `Method( param1, param2 )`
- Use K&R style braces (same line as declaration)
- Avoid `var`; prefer explicit types for clarity
- Prefix private instance fields with `_`, static fields with `s_`
- All public members require XML documentation comments

### Architecture
- Use base classes (`MusicLookupServiceBase`, `MediaLinkServiceBase`) for provider implementations
- Register auth handlers as Singleton, web services as Scoped, stateful services as Transient
- All HTTP clients must use `AddStandardResilience()` for retry/circuit breaker/timeout
- Use source-generated regex (`[GeneratedRegex]`) for performance

## Key Domain Concepts

### Music Matching
- Primary: Use ISRC (tracks, 12 chars) or UPC (albums, 12-13 digits) for cross-platform matching
- Fallback: Fuzzy title matching with `SanitizeSongTitle()`/`SanitizeAlbumTitle()` in `MusicLookupServiceBase`
- These methods remove "(Remastered)", "(Deluxe Edition)", etc.

### External APIs
- Apple MusicKit: JWT auth (ES256) via `AppleJwtHandler`, requires Team ID, Key ID, .p8 file
- Spotify Web API: OAuth 2.0 via `SpotifyTokenHandler` with token caching
- NetCord: Discord integration with `GuildMessages | MessageContent` intents

### URL Patterns
- Apple Music: `music.apple.com/{storefront}/{type}/{title}/{id}` → `AppleMusicLinkParser`
- Spotify: `open.spotify.com/{type}/{id}` → `SpotifyLinkParser`

## Common Tasks

### Adding a Music Provider
1. Add to `SupportedProviders` enum with `[Description]`
2. Inherit from `MusicLookupServiceBase`, implement `IMusicLookupService`
3. Register HTTP client in `StartupExtensions.AddTuneBridgeServices()`
4. Create auth handler if needed (`Domain/Implementations/Auth/`)
5. Update `AppExtensions.GetPrimaryProviderColor()`

### Testing
- Use MSTest framework with AAA pattern (Arrange, Act, Assert)
- Name tests: `MethodName_Scenario_ExpectedBehavior`
- Mock HTTP clients/APIs with Moq or NSubstitute

## Critical Guidelines

**HIGHEST PRIORITY - Communication**: 
- NEVER create documentation files (SECURITY_UPDATE.md, CHANGELOG.md, UPDATES.md, etc.) to communicate changes
- ALWAYS communicate updates through PR comments only
- This is a strict requirement

**Error Handling**:
- Public-facing endpoints: Return `null` when content not found (not exceptions) to prevent crashes
- Internal code: Let exceptions bubble up to loggable state quickly
- Fail fast on missing/invalid configuration (e.g., missing .p8 file)

**Security**:
- Never commit secrets or API credentials
- Use environment variables for sensitive configuration
- Load private keys (.p8 files) from filesystem, not embedded
