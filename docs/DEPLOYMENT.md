# Deployment guide

## Prerequisites

- Docker Engine with Docker Compose v2
- ports 80 and 443 available for Caddy
- DNS records for the public hosts configured in `docker-compose.yml`

## Install

Use the installer or download `docker-compose.yml` and `Caddyfile` directly. The installer creates
persistent bind-mount directories but does not create an environment or secrets directory.

```bash
curl -sSL https://raw.githubusercontent.com/tsmarvin/BridgeBeats/develop/containers/install.sh | bash
cd bridgebeats
```

Review every site-specific value in `docker-compose.yml`, including the public domains, AllowedHosts,
dashboard URL, certificate email, Cloudflare token, and optional PDS values. There is no `.env` or
other deployment setup file.

### Optional Redis password

BridgeBeats generates a cryptographically random Redis password on first start. To provide one,
export it through your secret manager and initialize the removable one-shot container first:

```bash
docker compose run --rm -e REDIS_PASSWORD bridgebeats-bootstrap
```

The value must contain 32–128 base64 or base64url characters. The initializer verifies a supplied value
against existing state and refuses mismatches.

## Start

```bash
docker compose up -d
```

The default stack starts BridgeBeats, Redis, and Caddy. On an empty or unusable database, BridgeBeats
exposes health endpoints and otherwise returns HTTP 503 `Configuration Required`. Seed the initial
record through normal .NET configuration before startup as described in
[Database-backed configuration](DATABASE_CONFIGURATION.md#one-time-startup-seeding).

## Optional bundled PDS

The third-party PDS is not required to open the BridgeBeats database and is not started by default.
Configure its server-level values in Compose, then start it when needed:

```bash
docker compose --profile pds up -d
```

The PDS daemon's JWT, PLC, administrator, and SMTP inputs are deployment infrastructure. The URI and
credentials BridgeBeats uses to connect to a PDS are stored in the encrypted application settings.

## Persistent state and backups

Back up these resources together:

- `./data/app`, including `bridgebeats.db`;
- `./data/dp-keys`, containing the Data Protection key ring;
- the `bridgebeats-bootstrap-secrets` named volume.

The database and Data Protection key ring form one recovery set. Losing or replacing the key ring
makes encrypted settings unreadable. The bootstrap volume retains Redis authentication state; Redis
and PDS data directories may also be backed up according to your recovery requirements.

See [Database-backed application configuration](DATABASE_CONFIGURATION.md) for the security model.
