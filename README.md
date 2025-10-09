# TuneBridge

## Docker Configuration

When running TuneBridge with Docker, you need to mount your Apple Music private key file into the container and configure the environment variable to point to the container path:

```bash
docker run -d \
  -v /path/to/your/AuthKey_KEYID.p8:/app/key.p8 \
  -e APPLE_TEAM_ID="your_team_id" \
  -e APPLE_KEY_ID="your_key_id" \
  -e APPLE_KEY_PATH="/app/key.p8" \
  -e SPOTIFY_CLIENT_ID="your_client_id" \
  -e SPOTIFY_CLIENT_SECRET="your_client_secret" \
  -e DISCORD_TOKEN="your_discord_token" \
  -p 10000:10000 \
  tunebridge
```

**Important:** The `APPLE_KEY_PATH` environment variable must match the container mount path. In the example above, both are set to `/app/key.p8`.