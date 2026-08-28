# Quick start

## Docker Compose

```bash
git clone https://github.com/tsmarvin/BridgeBeats.git
cd BridgeBeats/containers
```

Review every site-specific value in `docker-compose.yml`, including the public domains, AllowedHosts,
dashboard URL, certificate email, Cloudflare token, and any optional PDS settings. The Compose file
and `Caddyfile` are the complete deployment configuration surface; there is no application `.env`,
generated `appsettings.json`, or provider-secrets directory.

Optionally initialize a chosen Redis password before first start:

```bash
docker compose run --rm -e REDIS_PASSWORD bridgebeats-bootstrap
```

If `REDIS_PASSWORD` is not set, skip that command and BridgeBeats will generate one.

Start the default stack:

```bash
docker compose up -d
```

An empty or unusable deployment returns HTTP 503 `Configuration Required`. Seed the first settings
record through normal .NET configuration, then restart AppHost to activate that persisted revision
across Web and workers. See
[one-time startup seeding](DATABASE_CONFIGURATION.md#one-time-startup-seeding).

The bundled third-party PDS is optional and can be started later:

```bash
docker compose --profile pds up -d
```

See the [Deployment guide](DEPLOYMENT.md) and
[Database-backed configuration model](DATABASE_CONFIGURATION.md) for details.

## Local development

Provide a development Redis connection to AppHost, then run:

```bash
dotnet run --project src/BridgeBeats.AppHost
```

The local SQLite database is `bridgebeats.db` in the working directory. Application settings still
come from its encrypted aggregate rather than application configuration files.
