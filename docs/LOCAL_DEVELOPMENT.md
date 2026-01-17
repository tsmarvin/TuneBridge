# Local Development with Aspire

BridgeBeats uses [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) for local development orchestration, providing a rich dashboard with distributed tracing, structured logging, and resource monitoring.

## Prerequisites

- .NET 10.0 SDK or later
- Docker Desktop (for containerized dependencies)
- API credentials configured (see [Configuration](CONFIGURATION.md))

## Quick Start

From the repository root:

```bash
aspire run
```

The terminal will display a **Dashboard URL with authentication token**:

```
Dashboard:  https://localhost:17239/login?t=abc123tokenhere
```

Open this URL to access the Aspire Dashboard.

## Aspire Dashboard Features

The dashboard provides:

- **Resources View**: Monitor all application components and their health status
- **Structured Logs**: View application logs with filtering and search
- **Distributed Traces**: Trace requests across services
- **Metrics**: View application performance metrics

## Project Structure

The Aspire integration consists of:

| Project | Purpose |
|---------|---------|
| `BridgeBeats.AppHost` | Orchestration entry point - defines resources and dependencies |
| `BridgeBeats.ServiceDefaults` | Shared service configuration (OpenTelemetry, resilience, health checks) |

## Environment Variables

The AppHost passes all provider credentials to the application via environment variables. These can be set in:

1. **User Secrets** (recommended for development):
   ```bash
   dotnet user-secrets set "Spotify:ClientId" "your-client-id" --project src/BridgeBeats.csproj
   ```

2. **Environment Variables**: Set in your shell or IDE launch configuration

3. **appsettings.json**: For non-sensitive configuration only

## Aspire vs Production Docker Compose

| Aspect | Aspire (`aspire run`) | Docker Compose (`docker-compose up`) |
|--------|----------------------|--------------------------------------|
| **Purpose** | Local development & debugging | Production deployment |
| **Dashboard** | Built-in with full telemetry | Optional external dashboard |
| **Hot Reload** | Supported | Requires rebuild |
| **Reverse Proxy** | Not included | Caddy with SSL/TLS |
| **Secrets** | Environment/User Secrets | Docker secrets |

### When to Use Each

- **Use `aspire run`** for:
  - Day-to-day development
  - Debugging and testing
  - Exploring telemetry and logs

- **Use `docker-compose up`** for:
  - Production deployments
  - CI/CD pipeline testing
  - Replicating production environment locally

## Generating Deployment Artifacts

Aspire can generate Docker Compose files for deployment prototyping:

```bash
aspire publish
```

This outputs files to `aspire-output/`:
- `docker-compose.yaml` - Generated compose file
- `.env` - Environment configuration

> **Note**: The generated compose files are for development/prototyping. The production `docker-compose.yml` in the repository root is optimized for production use with Caddy reverse proxy, health checks, and proper secret management.

## Troubleshooting

### Port Conflicts

If you see port binding errors, ensure no other instances are running:

```bash
# Stop any running Aspire instances
# The CLI will prompt if an instance is already running
```

### Missing Dependencies

Ensure all prerequisites are installed:

```bash
dotnet --version  # Should be 10.0+
docker --version  # Docker must be running
```

### Dashboard Not Loading

1. Ensure you're using the full URL with the authentication token (`?t=...`)
2. Check that the terminal shows the application started successfully
3. Try stopping and restarting with `aspire run`

## Related Documentation

- [Configuration](CONFIGURATION.md) - Setting up API credentials
- [Deployment](DEPLOYMENT.md) - Production deployment guide
- [Quick Start](QUICKSTART.md) - 5-minute deployment guide
