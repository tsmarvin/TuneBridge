# SBOM and Provenance Documentation

BridgeBeats generates Software Bill of Materials (SBOM) and provenance attestations for all Docker images to improve supply chain security and transparency.

## What are SBOM and Provenance?

### Software Bill of Materials (SBOM)
An SBOM is a comprehensive inventory of all software components, libraries, and dependencies included in a Docker image. It helps you:
- Identify security vulnerabilities in dependencies
- Track licenses and compliance requirements
- Understand the complete software supply chain

### Provenance
Provenance attestations provide verifiable evidence about how, when, and where a Docker image was built. They include:
- Source repository and commit SHA
- Build timestamp and GitHub workflow details
- Build platform and environment information
- The exact build process used

## Accessing SBOM and Provenance

### Using Docker CLI

Docker 4.17+ includes built-in support for viewing attestations:

```bash
# View SBOM for BridgeBeats image
docker buildx imagetools inspect tsmarvin/bridgebeats:latest --format "{{ json .SBOM }}"

# View provenance for BridgeBeats image
docker buildx imagetools inspect tsmarvin/bridgebeats:latest --format "{{ json .Provenance }}"

# View SBOM for Caddy-Cloudflare image
docker buildx imagetools inspect tsmarvin/caddy-cloudflare:latest --format "{{ json .SBOM }}"

# View provenance for Caddy-Cloudflare image
docker buildx imagetools inspect tsmarvin/caddy-cloudflare:latest --format "{{ json .Provenance }}"
```

### Using Cosign

Install [cosign](https://github.com/sigstore/cosign) to verify attestations:

```bash
# Install cosign
# Install on macOS (via Homebrew)
brew install cosign

# Install on Linux (using package manager - recommended)
# For Debian/Ubuntu:
# sudo apt-get install cosign
# Or download binary (verify checksums from GitHub releases):
curl -LO https://github.com/sigstore/cosign/releases/latest/download/cosign-linux-amd64
chmod +x cosign-linux-amd64
sudo mv cosign-linux-amd64 /usr/local/bin/cosign

# Verify and view SBOM
cosign verify-attestation --type https://spdx.dev/Document \
  tsmarvin/bridgebeats:latest

# Verify and view provenance
cosign verify-attestation --type https://slsa.dev/provenance/v1 \
  tsmarvin/bridgebeats:latest
```

### Using Docker Scout

Docker Scout can analyze SBOMs for vulnerabilities:

```bash
# Enable Docker Scout
docker scout enable

# View vulnerabilities in BridgeBeats image
docker scout cves tsmarvin/bridgebeats:latest

# View detailed SBOM
docker scout sbom tsmarvin/bridgebeats:latest
```

## Verifying Provenance

Provenance attestations allow you to verify:

1. **Source Repository**: Confirm the image was built from the official repository
2. **Commit SHA**: Trace back to the exact source code version
3. **Build Workflow**: Verify the build process used
4. **Build Time**: Check when the image was created

Example provenance verification:

```bash
# Extract provenance details (requires jq - install with: apt-get install jq / brew install jq)
docker buildx imagetools inspect tsmarvin/bridgebeats:latest \
  --format "{{ json .Provenance }}" | jq '.payload' | base64 -d | jq
```

Look for these key fields:
- `builder.id`: GitHub Actions workflow that built the image
- `metadata.buildInvocationID`: Unique build identifier
- `metadata.completeness`: Build provenance completeness indicators
- `materials`: Source repository and commit information

## SBOM Format

SBOMs are generated in SPDX format, which includes:
- Package names and versions
- Package licenses
- Dependency relationships
- File checksums

## CI/CD Integration

SBOM and provenance are automatically generated for:
- All pushes to `main` branch (tagged as `latest`)
- All pushes to `develop` branch (tagged as `develop`)
- All version tags (e.g., `v1.0.0`)
- Pull requests (for testing)

### Workflows with SBOM/Provenance

1. **docker-publish.yml** - BridgeBeats application image
   - Image: `tsmarvin/bridgebeats`
   - Platforms: linux/amd64, linux/arm64

2. **publish-caddy-cloudflare.yml** - Caddy with Cloudflare DNS
   - Image: `tsmarvin/caddy-cloudflare`
   - Platforms: linux/amd64, linux/arm64

## Security Best Practices

1. **Verify Before Use**: Always verify attestations before deploying images in production
2. **Monitor Vulnerabilities**: Use Docker Scout or similar tools to scan SBOMs for known CVEs
3. **Track Dependencies**: Review SBOMs to understand your supply chain
4. **Audit Build Process**: Use provenance to ensure images come from trusted sources

## Troubleshooting

### Attestations Not Found

If attestations are missing:
1. Ensure you're using a recent image (built after SBOM/provenance was enabled)
2. Check that you have Docker 4.17+ or cosign installed
3. Verify you're using the correct image name and tag

### Verification Failures

If verification fails:
1. Ensure the image was built through official GitHub Actions workflows
2. Check that you have the latest version of verification tools
3. Verify your Docker registry credentials if accessing private images

## Additional Resources

- [Docker BuildKit Attestations](https://docs.docker.com/build/attestations/)
- [SLSA Provenance Specification](https://slsa.dev/provenance/v1)
- [SPDX Specification](https://spdx.dev/specifications/)
- [Sigstore Cosign](https://github.com/sigstore/cosign)
- [Docker Scout Documentation](https://docs.docker.com/scout/)

## Questions?

For questions or issues related to SBOM and provenance:
1. Check the [Issues](https://github.com/tsmarvin/BridgeBeats/issues) page
2. Review GitHub Actions workflow logs
3. Open a new issue with details about your verification attempt
