# Caddy and Cloudflare

Caddy terminates public HTTPS before BridgeBeats can expose its setup or application UI. Its public
domain, short domain, certificate email, PDS hostname, and Cloudflare DNS token are therefore
deployment-time inputs in `docker-compose.yml`, not database-backed application settings.

The Cloudflare token should be scoped to DNS record editing for the relevant zone. Set it directly in
the Compose service environment before deployment and do not commit a real token. Caddy consumes it
as `CLOUDFLARE_API_TOKEN` for DNS challenges used by the dashboard and wildcard PDS routes. There is
no companion `.env` or generated setup file.

The BridgeBeats Web and worker processes never receive this token.

After changing Caddy inputs, recreate the proxy:

```bash
docker compose up -d --force-recreate bridgebeats-caddy
```

The bundled PDS is an optional profile and may be started after application configuration:

```bash
docker compose --profile pds up -d
```
