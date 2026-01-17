# BridgeBeats

[![Build and Deploy Documentation](https://github.com/tsmarvin/BridgeBeats/actions/workflows/docs.yml/badge.svg)](https://github.com/tsmarvin/BridgeBeats/actions/workflows/docs.yml)
[![docker-publish](https://github.com/tsmarvin/BridgeBeats/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/tsmarvin/BridgeBeats/actions/workflows/docker-publish.yml)
[![Tests](https://github.com/tsmarvin/BridgeBeats/actions/workflows/tests.yml/badge.svg)](https://github.com/tsmarvin/BridgeBeats/actions/workflows/tests.yml)
[![CodeQL](https://github.com/tsmarvin/BridgeBeats/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/tsmarvin/BridgeBeats/actions/workflows/github-code-scanning/codeql)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/tsmarvin/BridgeBeats/badge)](https://scorecard.dev/viewer/?uri=github.com/tsmarvin/BridgeBeats)

**Music is universal. Your links should be too.**

BridgeBeats is a cross-platform music link converter that helps you share music effortlessly across streaming platforms. Share a link from Apple Music, Spotify, or Tidal, and BridgeBeats finds the same track or album on all supported services—ensuring every listener can enjoy the music, regardless of their preferred platform.

## ✨ Share once. Play anywhere.

Ever wanted to share your favorite song, only to realize your friend uses a different streaming service? BridgeBeats solves this by automatically finding the same track or album on all major platforms—so everyone can listen, no matter where they stream.

## 🎵 Features

- **Instant Conversion** - Drop a music link and get matches across Apple Music, Spotify, and Tidal
- **Discord Bot** - Automatically converts music links in your Discord server
- **Web Interface** - Simple browser-based tool for quick conversions
- **RESTful API** - Integrate music link conversion into your own apps
- **Accurate Matching** - Uses ISRC (tracks) and UPC (albums) for precise cross-platform matches
- **Rich Previews** - OpenGraph cards that work everywhere—Discord, Slack, Twitter, and more
- **Aspire Dashboard** - Built-in observability and telemetry dashboard (role-based access)

## 🚀 Quick Start

### Using Docker Compose (Recommended)

Get started in 5 minutes with automatic HTTPS:

```bash
git clone https://github.com/tsmarvin/BridgeBeats.git
cd BridgeBeats
./setup-secrets.sh
nano apple_key.p8
nano atproto_password.txt
nano discord_token.txt
nano spotify_client_secret.txt
nano tidal_client_secret.txt
# Edit secrets/ with your credentials
cp .env.example .env
nano .env
# Edit .env with your configuration
docker compose up -d
```

Visit `https://localhost` to start converting links.

📖 **[Complete Quick Start Guide →](QUICKSTART.md)**

### Using Docker

```bash
docker run -p 10000:10000 \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  ghcr.io/tsmarvin/bridgebeats:latest
```

Visit `http://localhost:10000` to start converting links.

### Running Locally

1. Install [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10)
2. Clone the repository
3. Add your API credentials to `appsettings.json` (see [Configuration Guide](docs/CONFIGURATION.md))

**Option A: Using .NET Aspire (Recommended)**

```bash
aspire run
```

This starts the app with the Aspire Dashboard for monitoring, tracing, and structured logging. See the [Local Development Guide](docs/LOCAL_DEVELOPMENT.md) for details.

**Option B: Direct .NET Run**

```bash
dotnet run
```

## 📁 Project Structure

```
BridgeBeats/
├── src/
│   ├── BridgeBeats.Web/             # ASP.NET Core web application (MVC, controllers, views)
│   ├── BridgeBeats.Services/        # Business logic and service layer
│   ├── BridgeBeats.Providers/       # Music provider integrations (Apple Music, Spotify, Tidal)
│   ├── BridgeBeats.Infrastructure/  # Data access, caching, identity, storage
│   ├── BridgeBeats.Contracts/       # Shared DTOs, interfaces, enums, constants
│   ├── BridgeBeats.AppHost/         # .NET Aspire orchestration
│   └── BridgeBeats.ServiceDefaults/ # Shared service configuration (telemetry, resilience)
├── Tests/                           # Test project
│   ├── Unit/                        # Unit tests
│   ├── Integration/                 # Integration tests (service interactions)
│   └── EndToEnd/                    # End-to-end tests (full request/response flows)
├── docs/                            # Documentation (guides, API reference)
├── .github/                         # GitHub templates and workflows
└── docker-compose.yml               # Production deployment configuration
```

## 📖 Documentation

- **[Quick Start Guide](docs/QUICKSTART.md)** - Get up and running in 5 minutes
- **[Local Development Guide](docs/LOCAL_DEVELOPMENT.md)** - Develop with .NET Aspire Dashboard
- **[Configuration Guide](docs/CONFIGURATION.md)** - Set up API credentials and environment variables
- **[API Reference](docs/API.md)** - Integrate BridgeBeats into your applications
- **[Deployment Guide](docs/DEPLOYMENT.md)** - Deploy to Docker, Kubernetes, or cloud platforms
- **[SBOM and Provenance Guide](docs/SBOM_AND_PROVENANCE.md)** - Verify supply chain security and inspect dependencies
- **[Caddy Cloudflare Guide](docs/CADDY_CLOUDFLARE.md)** - Configure Cloudflare DNS for wildcard certificates
- **[Contributing Guidelines](CONTRIBUTING.md)** - Learn how to contribute to BridgeBeats
- **[Caching Guide](docs/CACHING.md)** - Configure ATProto PDS caching
- **[ATProto Lexicon Setup](docs/ATPROTO_LEXICON.md)** - Configure lexicon resolution and DNS for ATProto compliance

## 🎯 How It Works

BridgeBeats connects to official APIs from music streaming services. When you provide a link:

1. **Extract** - Identifies the track or album from the URL
2. **Match** - Uses external IDs (ISRC/UPC) or metadata to find equivalents
3. **Return** - Provides links for all available platforms

The result? You share the music, not the platform.

## 🤖 Discord Bot

Add BridgeBeats to your Discord server to automatically convert music links in conversations:

1. Get a Discord bot token (see [Configuration Guide](docs/CONFIGURATION.md#discord-bot-token))
2. Set `DISCORD_TOKEN` environment variable
3. Invite the bot to your server

When someone shares a Spotify link, BridgeBeats responds with a card showing Apple Music and Tidal alternatives—and vice versa.

## 🛠️ Built With

- **.NET 10.0** - Modern, cross-platform framework
- **Apple MusicKit API** - Apple Music integration
- **Spotify Web API** - Spotify integration
- **Tidal API** - Tidal integration
- **NetCord** - Discord bot library

## 🔐 Privacy & Security

- API keys are hashed and never stored in plain text
- Input URLs with tracking parameters are kept private (not stored on ATProto PDS)
- Rate limiting ensures fair usage (20 requests/hour per user)
- All credentials configured via environment variables

## 🌍 Deployment Options

BridgeBeats can be deployed anywhere:

- **Docker** - Simple containerized deployment
- **Cloud Services** - Azure Container Apps, AWS ECS, Google Cloud Run
- **Kubernetes** - Scalable orchestration
- **Self-hosted** - Run natively on Linux, Windows, or macOS

See the [Deployment Guide](docs/DEPLOYMENT.md) for platform-specific instructions.

## 📊 API Usage

Create an account to get your API key:

```bash
curl -X POST http://localhost:10000/account/register \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com","password":"SecurePassword123"}'
```

Convert a link:

```bash
curl -X POST http://localhost:10000/music/lookup/urlList \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_API_KEY" \
  -d '{"uri":"https://open.spotify.com/track/..."}'
```

See the [API Reference](docs/API.md) for complete documentation.

## 🧪 Testing

```bash
# Run all tests
dotnet test

# Run unit tests only (no API credentials needed)
dotnet test --filter "FullyQualifiedName~Unit"
```

For more details on testing, see the [Contributing Guidelines](CONTRIBUTING.md#testing-guidelines).

## 🤝 Contributing

We welcome contributions! Whether you're fixing bugs, adding features, improving documentation, or helping others, your contributions make BridgeBeats better for everyone.

Please read our [Contributing Guidelines](CONTRIBUTING.md) to get started. Key points:

- Follow our [Code of Conduct](CODE_OF_CONDUCT.md)
- Check existing issues before opening a new one
- Use our issue templates for bug reports and feature requests
- Keep pull requests focused and well-tested
- Respect our security practices - see [Security Policy](SECURITY.md)

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 👤 Author

**Taylor Marvin**

---

*Because music connects us—no matter where we listen.*
