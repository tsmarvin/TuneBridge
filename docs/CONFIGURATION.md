# Configuration guide

BridgeBeats stores application configuration in one encrypted database aggregate. AppHost may use
normal .NET configuration once to seed an empty database; after that, Web and workers consume only
the persisted startup snapshot.

See [Database-backed application configuration](DATABASE_CONFIGURATION.md) for the authoritative
startup, activation, security, and recovery model.

## Database settings

The aggregate contains:

- music-provider identifiers, credentials, and enablement flags;
- ATProto service-account, PDS connection, and OAuth settings;
- Discord credentials;
- request limits;
- cache, queue, maintenance, Spotify batch, and HTTP resilience tuning;
- internal-service authentication and API-key hashing secrets.

Sensitive values are encrypted as one protected secrets document. Ordinary reads expose only
configured/not-configured status; plaintext is available only to authorized runtime consumers.

Settings writes are durable immediately, but the AppHost must be restarted to activate a new
revision across Web and workers. A process never combines values from different revisions.

## Bootstrap and deployment inputs

The production image uses fixed internal locations and service addresses:

- SQLite: `/app/data/bridgebeats.db`
- Redis: `redis:6379`
- Data Protection key ring: `/app/keys`, bind-mounted from `./data/dp-keys`
- logs and Aspire state: `/app/data`

The initializer generates only the Redis password in a named volume. ASP.NET Core manages its
persistent key ring directly in `./data/dp-keys`; keep that directory with the application database.
To choose the initial Redis password, set `REDIS_PASSWORD` to 32–128 base64 or base64url characters
and run:

```bash
docker compose run --rm -e REDIS_PASSWORD bridgebeats-bootstrap
```

If this step is skipped, `docker compose up` generates a password. A later mismatching value is
rejected; password rotation is never implicit.

Caddy domains, application domain, allowed hosts, and certificate infrastructure must be configured directly in `docker-compose.yml`
before the public proxy starts. The Compose file and `Caddyfile` are the complete deployment
configuration surface; there is no companion `.env` or generated setup file.

## Initial setup

Seed an empty database from `dotnet user-secrets`, environment variables, or command-line
configuration by enabling `BridgeBeats:Bootstrap:SeedSettings`. The seed requires an API-key salt
and at least one complete provider. See
[Database-backed application configuration](DATABASE_CONFIGURATION.md#one-time-startup-seeding) for
the exact precedence, validation, and no-overwrite behavior. Without a valid seed, only Web starts
and non-health requests return HTTP 503 `Configuration Required`.
