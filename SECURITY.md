# Security Policy

## Supported Versions

BridgeBeats follows a rolling release model. We recommend always using the latest version for the best security and features.

| Version | Supported          |
| ------- | ------------------ |
| latest  | :white_check_mark: |
| < latest | :x:               |

**Docker Images**: We publish security-patched images regularly. Always pull the `latest` tag or use specific version tags for reproducible deployments.

## Reporting a Vulnerability

We take security seriously. If you discover a security vulnerability in BridgeBeats, please report it responsibly.

### How to Report

**Please do NOT create a public GitHub issue for security vulnerabilities.**

Instead, report security issues through one of these channels:

1. **GitHub Security Advisories** (Preferred)
   - Navigate to the [Security tab](https://github.com/tsmarvin/BridgeBeats/security/advisories)
   - Click "Report a vulnerability"
   - Provide detailed information about the vulnerability

2. **Email**
   - Contact the maintainer directly (see repository profile)
   - Include "BridgeBeats Security" in the subject line
   - Encrypt sensitive information if possible

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

## Security Best Practices

### For Deployment

1. **Credentials Management**
   - Never commit API keys, tokens, or passwords to source control
   - Use environment variables or Docker secrets for all credentials
   - Rotate API keys and secrets regularly (at least every 90 days)
   - Use the provided `setup-secrets.sh` script for proper secret file permissions

2. **API Key Security**
   - Generate a strong, random API key salt using: `openssl rand -base64 32`
   - Store the salt securely and never share it publicly
   - API keys are hashed before storage using the salt

3. **ATProto Integration**
   - Use ATProto app passwords, not your main account password
   - Generate dedicated app passwords at: Settings → App Passwords
   - Limit app password permissions to minimum required scope

4. **HTTPS in Production**
   - Always deploy behind a reverse proxy with TLS/HTTPS enabled
   - Use the included Caddy configuration for automatic HTTPS with Let's Encrypt
   - Configure proper SSL/TLS certificates for production domains

5. **Network Security**
   - Configure `ALLOWED_HOSTS` to restrict allowed hostnames
   - Use firewall rules to limit access to sensitive endpoints
   - Consider rate limiting at the reverse proxy level for additional protection

6. **Docker Security**
   - Run containers as non-root users when possible
   - Use Docker secrets for sensitive data in Docker Swarm
   - Keep base images updated (multi-stage builds use latest .NET images)
   - Limit container capabilities and resources

7. **Database Security**
   - Use strong passwords for database connections (if using external databases)
   - Regularly back up SQLite databases (contains user data and API keys)
   - Restrict file system permissions on database files (600 or 640)

### For Development

1. **Dependency Updates**
   - Dependabot automatically checks for security updates weekly
   - Review and merge Dependabot PRs promptly
   - Monitor GitHub Security Advisories for dependency vulnerabilities

2. **Code Scanning**
   - CodeQL scanning is enabled via GitHub Advanced Security
   - Address CodeQL findings before merging pull requests
   - OpenSSF Scorecard provides supply chain security insights

3. **Secure Coding**
   - Enable nullable reference types (`<Nullable>enable</Nullable>`)
   - Treat warnings as errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)
   - Follow principle of least privilege for service integrations
   - Validate and sanitize all user inputs

4. **Testing**
   - Write security tests for authentication and authorization
   - Test with various input types (including malicious inputs)
   - Use the test suite to prevent security regressions

## Security Features

BridgeBeats includes several built-in security features:

### Authentication & Authorization
- API key-based authentication with hashed storage
- ASP.NET Core Identity for user management
- Role-based access control for Aspire Dashboard
- Rate limiting (20 requests/hour per user by default)

### Data Protection
- API keys are hashed with salt before storage
- Links with tracking parameters kept private (not stored on ATProto PDS)
- Secure credential handling via environment variables
- Docker secrets support for production deployments

### HTTP Security
- HTTPS enforcement via reverse proxy (Caddy)
- Standard security headers (configured in reverse proxy)
- CORS configuration for API access
- Request validation and sanitization

### Monitoring & Observability
- OpenTelemetry integration for security event logging
- File-based logging with automatic rotation (up to ~50MB)
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

- **No Telemetry**: BridgeBeats does not collect usage telemetry or analytics
- **Minimal Data Storage**: Only stores user accounts, API keys (hashed), and optional link cache
- **No Tracking**: Links with tracking parameters are kept private and not shared on ATProto PDS
- **Open Source**: Full transparency - you can audit the code yourself

## Security Updates

Security updates are distributed through:

1. **Docker Images** - Published to GitHub Container Registry (ghcr.io)
2. **GitHub Releases** - Tagged releases with compiled binaries
3. **Source Code** - Always available on the main branch

Subscribe to repository notifications to receive security advisories and release announcements.

## Responsible Disclosure

We follow responsible disclosure practices:

- Security issues are fixed before public disclosure
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
