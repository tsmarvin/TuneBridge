# SBOM and Provenance Documentation

BridgeBeats generates Software Bill of Materials (SBOM) and provenance attestations for its Docker images to improve supply chain security and transparency.

## What are SBOM and Provenance?

### Software Bill of Materials (SBOM)
An SBOM is an inventory of the software components, libraries, and dependencies included in a Docker image. It helps you:
- Identify security vulnerabilities in dependencies
- Track licenses and compliance requirements
- Understand the software supply chain

### Provenance
Provenance attestations provide verifiable evidence about how, when, and where a Docker image was built. They include:
- Source repository and commit SHA
- Build timestamp and GitHub workflow details
- Build platform and environment information
- The build process used

## How BridgeBeats produces these attestations

The build-and-push workflows (`docker-publish.yml` and `publish-caddy-cloudflare.yml`) call `docker/build-push-action` with `sbom: true` and `provenance: mode=max`. BuildKit attaches the resulting SBOM and SLSA provenance as in-toto attestations to each image, and the workflow pushes per-architecture images by digest before assembling a multi-arch manifest.

> **Verify (compliance note):** the workflows do **not** run a separate `cosign sign` step. The attestations are BuildKit/in-toto attestations carried with the image in the registry, inspectable with `docker buildx imagetools inspect`. They are not Sigstore-signed via cosign keyless signing. The "Using Cosign" section below documents the cosign verification commands, but `cosign verify-attestation` expects a cosign-signed attestation; confirm cosign verification against a published image before treating it as a control, or use `docker buildx imagetools inspect` (the source-confirmed path) instead.

## Accessing SBOM and Provenance

### Using Docker CLI (source-confirmed path)

Recent Docker/Buildx releases can view the attestations attached to the image:

```bash
# View SBOM for the BridgeBeats image
docker buildx imagetools inspect tsmarvin/bridgebeats:latest --format "{{ json .SBOM }}"

# View provenance for the BridgeBeats image
docker buildx imagetools inspect tsmarvin/bridgebeats:latest --format "{{ json .Provenance }}"

# View SBOM for the Caddy-Cloudflare image
docker buildx imagetools inspect tsmarvin/caddy-cloudflare:latest --format "{{ json .SBOM }}"

# View provenance for the Caddy-Cloudflare image
docker buildx imagetools inspect tsmarvin/caddy-cloudflare:latest --format "{{ json .Provenance }}"
```

### Using Cosign

[cosign](https://github.com/sigstore/cosign) can verify attestations on signed images:

```bash
# Install on macOS (Homebrew)
brew install cosign

# Install on Linux (download binary; verify checksums from GitHub releases)
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

> **Verify:** these commands succeed only if the image carries cosign-signed attestations. The current workflows produce BuildKit attestations without a cosign signing step, so `cosign verify-attestation` may report no matching signatures. Use `docker buildx imagetools inspect` (above) to read the BuildKit SBOM/provenance directly.

### Using Docker Scout

Docker Scout can analyze the image for vulnerabilities:

```bash
# Enable Docker Scout
docker scout enable

# View CVEs in the BridgeBeats image
docker scout cves tsmarvin/bridgebeats:latest

# View the SBOM
docker scout sbom tsmarvin/bridgebeats:latest
```

## Reading Provenance

Provenance attestations let you confirm:

1. **Source repository**: the image was built from the official repository
2. **Commit SHA**: trace back to the exact source code version
3. **Build workflow**: the build process used
4. **Build time**: when the image was created

Extract and decode the provenance payload:

```bash
# Requires jq (apt-get install jq / brew install jq)
docker buildx imagetools inspect tsmarvin/bridgebeats:latest \
  --format "{{ json .Provenance }}" | jq '.payload' | base64 -d | jq
```

Look for fields such as:
- `builder.id`: the GitHub Actions workflow that built the image
- `metadata`: build invocation and completeness indicators
- `materials` / `buildDefinition`: source repository and commit information

> **Verify:** exact field names depend on the SLSA provenance version BuildKit emits. Inspect the decoded payload against the [SLSA provenance spec](https://slsa.dev/provenance/v1) for your build.

## SBOM Format

BuildKit emits the SBOM in SPDX format, which includes:
- Package names and versions
- Package licenses
- Dependency relationships
- File checksums

## CI/CD Integration

SBOM and provenance attestations are generated for:
- Pushes to `main` (tagged `latest`)
- Pushes to `develop` (tagged `develop`)
- Pull requests (tagged `pr-<number>`, for testing)

Version tags (`v*.*.*`) trigger the application image workflow as well.

### Workflows with SBOM/Provenance

1. **docker-publish.yml** — BridgeBeats application image
   - Image: `tsmarvin/bridgebeats`
   - Platforms: `linux/amd64`, `linux/arm64`
   - Triggers: push to `main`/`develop`, `v*.*.*` tags, and pull requests

2. **publish-caddy-cloudflare.yml** — Caddy with Cloudflare DNS
   - Image: `tsmarvin/caddy-cloudflare`
   - Platforms: `linux/amd64`, `linux/arm64`
   - Triggers: changes to `containers/Dockerfile.caddy` or the workflow file on `main`/`develop`, pull requests, and manual dispatch

## Security Best Practices

1. **Verify before use**: inspect attestations before deploying images in production
2. **Monitor vulnerabilities**: use Docker Scout or a similar tool to scan the SBOM for known CVEs
3. **Track dependencies**: review the SBOM to understand your supply chain
4. **Audit the build**: use provenance to confirm images come from the official workflow

## Troubleshooting

### Attestations Not Found

If attestations are missing:
1. Ensure you are using an image built after SBOM/provenance was enabled
2. Use a recent Docker/Buildx release that can read attestations
3. Verify the image name and tag are correct

### Verification Failures

If verification fails:
1. Confirm the image was built through the official GitHub Actions workflow
2. Update your verification tools to the latest version
3. If using cosign, remember the current workflows produce BuildKit attestations without a cosign signature — prefer `docker buildx imagetools inspect`

## Additional Resources

- [Docker BuildKit Attestations](https://docs.docker.com/build/attestations/)
- [SLSA Provenance Specification](https://slsa.dev/provenance/v1)
- [SPDX Specification](https://spdx.dev/specifications/)
- [Sigstore Cosign](https://github.com/sigstore/cosign)
- [Docker Scout Documentation](https://docs.docker.com/scout/)

## Questions?

For questions or issues related to SBOM and provenance:
1. Check the [Issues](https://github.com/tsmarvin/BridgeBeats/issues) page
2. Review the GitHub Actions workflow logs
3. Open a new issue with details about your verification attempt
