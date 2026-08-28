# Database-backed application configuration

BridgeBeats treats the encrypted `ApplicationSettings` database record as the authoritative source
for application and provider configuration after first-run seeding. Production child processes do
not load provider credentials or operational tuning from `appsettings.json`, `.env`, user secrets,
or mounted provider-secret files.

## Startup model

AppHost opens the SQLite database, applies pending migrations, decrypts the current settings record,
and captures one revision before it composes the process topology. It projects that same snapshot to
Web and every enabled worker. Credentials cannot be overridden by child-process configuration.

Changes made through `IApplicationSettingsService` are durable immediately. Process composition,
provider enablement, and singleton options use the startup snapshot, so a controlled AppHost restart
is required to activate a newer revision. This explicit restart boundary prevents different processes
from observing different revisions.

When the database has no settings record and startup seeding is disabled, AppHost starts only Web.
Web exposes its health endpoints and otherwise returns HTTP 503 with `Configuration Required`; it
does not start Redis-backed application services, provider workers, or identity.

## One-time startup seeding

AppHost can create the first settings record from normal .NET configuration. Set
`BridgeBeats:Bootstrap:SeedSettings` to `true` and provide a complete settings graph through
`dotnet user-secrets`, environment variables, or command-line arguments. At minimum, configure a
32-character `BridgeBeats:ApiKeySalt` and one complete provider credential group.

```bash
dotnet user-secrets set "BridgeBeats:Bootstrap:SeedSettings" "true" \
  --project src/BridgeBeats.AppHost/BridgeBeats.AppHost.csproj
dotnet user-secrets set "BridgeBeats:ApiKeySalt" "replace-with-at-least-32-random-characters" \
  --project src/BridgeBeats.AppHost/BridgeBeats.AppHost.csproj
dotnet user-secrets set "BridgeBeats:SpotifyClientId" "your-client-id" \
  --project src/BridgeBeats.AppHost/BridgeBeats.AppHost.csproj
dotnet user-secrets set "BridgeBeats:SpotifyClientSecret" "your-client-secret" \
  --project src/BridgeBeats.AppHost/BridgeBeats.AppHost.csproj
```

Apple Music requires `AppleTeamId`, `AppleKeyId`, and PEM contents in `ApplePrivateKey`; a legacy
`AppleKeyPath` is not imported. Tidal requires its client ID and secret together. ATProto requires
the identifier, password, and user DID together. A Discord token additionally requires a
32-character internal-service key.

The seed is validated and encrypted through `IApplicationSettingsService`. It runs only when the
database has no settings row. Once any row exists, startup configuration cannot update or replace
it, even while the seed flag remains enabled. AppHost then reloads the persisted revision and
projects only that snapshot to Web and workers; plaintext values are never logged.

Environment variables use the standard double-underscore form, for example
`BridgeBeats__Bootstrap__SeedSettings` and `BridgeBeats__SpotifyClientSecret`. CI supplies these
only to the test steps. The Web test factory uses the same seed-and-project code against an isolated
SQLite database.

## Bootstrap-only values

The SQLite location, Redis host/port, Data Protection paths, logging path, node number, public
domain, allowed hosts, and dashboard URL are deployment inputs rather than database runtime
settings. Standard SQLite always uses `/app/data/bridgebeats.db` in the production image. Domain
and allowed-host changes are applied through Compose/Caddy configuration and a controlled restart.

The Compose stack creates a random Redis password in the `bridgebeats-bootstrap-secrets` named
volume. The persistent ASP.NET Core Data Protection key ring lives in the `./data/dp-keys` bind
mount and is required before AppHost starts. Data Protection creates and rotates its standard key
files there; BridgeBeats does not add a separate certificate-encryption layer to the key ring.

An operator may supply `REDIS_PASSWORD` during the first bootstrap. It must contain 32–128 base64 or
base64url characters. Run `docker compose run --rm -e REDIS_PASSWORD bridgebeats-bootstrap`
before the first `docker compose up`; the one-shot container is removed so the supplied value is not
retained in container metadata. If the secret is omitted, the initializer generates a
cryptographically random password. Supplying a different value after initialization is rejected
rather than rotating the credential implicitly.

The initializer never prints secret values. Provider secrets, the internal-service key, and the API
key salt are encrypted inside SQLite by ASP.NET Core Data Protection.

BridgeBeats uses standard SQLite, not SQLCipher, so there is no database-password setting. Database
confidentiality depends on host-volume access controls plus the separately persisted Data Protection
key material. The Redis password is bootstrap infrastructure and is never stored in the application
settings row.

## External deployment infrastructure

Caddy's public domains, certificate email, and Cloudflare token must be available before the reverse
proxy starts, so they remain explicit Compose inputs. They are not application runtime settings.

The bundled third-party PDS is opt-in through the `pds` Compose profile and is not started by default.
It may be started after BridgeBeats has loaded its database settings. Its server-level JWT, PLC,
administrator, and SMTP values configure the PDS daemon itself; the URI and credentials BridgeBeats
uses to connect to a PDS remain in the encrypted application settings aggregate.

## Backup and recovery

Back up these items as one recovery set:

- the SQLite database under `/app/data`;
- the Data Protection key ring under `./data/dp-keys`; and
- the `bridgebeats-bootstrap-secrets` volume.

Losing the Data Protection key ring makes protected settings unreadable. Replacing it while retaining
the old database is not a password reset and cannot recover the ciphertext. Access to the database and
key ring is sufficient to decrypt stored application secrets, so protect backups accordingly. Preserve
the key ring when moving an existing deployment.
