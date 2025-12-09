# Security Policy

## Supported Versions

Security updates are provided for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| 1.0.0+  | :white_check_mark: |
| < 1.0.0 | :x:               |

**Docker Images**: We publish security-patched images regularly. Always pull the `latest` tag or use specific version tags for reproducible deployments.

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in BridgeBeats, please report it responsibly.

### How to Report

Please report security vulnerabilities by creating a [GitHub issue](https://github.com/tsmarvin/BridgeBeats/issues).

If you prefer not to use GitHub issues, you can email: **admin@bridgebeats.link**

### What to Include

When reporting a vulnerability, please include:

- **Description** - Clear description of the vulnerability
- **Impact** - Potential impact and attack scenarios
- **Reproduction Steps** - Step-by-step instructions to reproduce the issue
- **Affected Versions** - Which versions are affected (if known)
- **Suggested Fix** - If you have ideas for remediation (optional)

### Response Timeline

- **Initial Response**: Within 48 hours
- **Status Update**: Within 7 days
- **Fix Timeline**: Depends on severity
  - Critical: 1-7 days
  - High: 7-14 days
  - Medium: 14-30 days
  - Low: 30-90 days

We'll keep you informed throughout the process and credit you in the security advisory (unless you prefer to remain anonymous).

## Security Features

BridgeBeats includes several built-in security features:

### Authentication & Authorization
- API key-based authentication with hashed storage
- ASP.NET Core Identity for user management
- Role-based access control for Aspire Dashboard
- Rate limiting (20 requests/hour per user by default)

### Data Protection
- API keys are hashed with salt before storage
- Input links kept private (not stored on ATProto PDS, only in local cache)

### HTTP Security
- HTTPS enforcement via reverse proxy (Caddy)
- Standard security headers (configured in reverse proxy)
- CORS configuration for API access
- Request validation and sanitization

### Monitoring & Observability
- OpenTelemetry integration for security event logging
- File-based logging with automatic rotation (up to ~50MB total across all retained log files)
- Health check endpoints for monitoring
- Failed authentication attempts logged for auditing

### Supply Chain Security
- **Dependabot** - Automated dependency updates (weekly scans)
- **CodeQL** - Automated code security scanning via GitHub Advanced Security
- **OpenSSF Scorecard** - Supply chain security assessment
- Pinned GitHub Actions with SHA hashes
- Multi-stage Docker builds with minimal attack surface

## Configuration Validation

BridgeBeats performs fail-fast validation at startup:

- Checks for required music provider credentials
- Validates Apple Music private key (.p8) file existence
- Confirms API key salt is configured
- Warns about missing optional features (Discord, ATProto)

Missing or invalid credentials are logged at startup, helping identify configuration issues early.

## Privacy Considerations

- **Minimal Data Storage**: Only stores user accounts, API keys (hashed), and optional link cache
- **No Tracking**: Input links are kept private and not shared on ATProto PDS
- **Open Source**: Full transparency - you can audit the code yourself

## Security Updates

Security updates are distributed through:

1. **Docker Images** - Published to Docker Hub
2. **Source Code** - Always available on the main branch

Subscribe to repository notifications to receive security advisories and release announcements.

## Responsible Disclosure

We follow responsible disclosure practices:

- Security issues are privately reported and fixed before public disclosure
- Security advisories are published after fixes are available
- CVE IDs are requested for significant vulnerabilities
- Contributors are credited (unless they prefer anonymity)

## Additional Resources

- [Configuration Guide](docs/CONFIGURATION.md) - Secure configuration instructions
- [Deployment Guide](docs/DEPLOYMENT.md) - Production deployment security
- [Caddy Cloudflare Guide](docs/CADDY_CLOUDFLARE.md) - HTTPS and DNS security
- [OpenSSF Scorecard](https://scorecard.dev/viewer/?uri=github.com/tsmarvin/BridgeBeats) - Supply chain security metrics

## Acknowledgments

We appreciate security researchers who responsibly disclose vulnerabilities and help make BridgeBeats more secure.

---

**Last Updated**: December 2025
