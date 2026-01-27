# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1aacc8154bc3071349907dae26849df301188be1a2e1f4560b903fb6275e481a AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:25d14b400b75fa4e89d5bd4487a92a604a4e409ab65becb91821e7dc4ac7f81f AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH

WORKDIR /src
COPY src/ .
COPY containers/Test-LockFileChanges.ps1 /scripts/Test-LockFileChanges.ps1

# Handle platform-specific lock file for AppHost
# The AppHost uses platform-specific Aspire packages (Dashboard.Sdk, Hosting.Orchestration)
# We need to regenerate the lock file for ARM64 builds and verify only platform packages changed
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        echo "ARM64 build detected - regenerating AppHost lock file for platform-specific packages..."; \
        cp BridgeBeats.AppHost/packages.lock.json BridgeBeats.AppHost/packages.lock.json.backup; \
        dotnet restore "BridgeBeats.AppHost/BridgeBeats.AppHost.csproj" --force-evaluate; \
        pwsh /scripts/Test-LockFileChanges.ps1 \
            -BackupPath BridgeBeats.AppHost/packages.lock.json.backup \
            -CurrentPath BridgeBeats.AppHost/packages.lock.json; \
    fi


# Restore all projects needed by the AppHost
RUN rm /scripts/Test-LockFileChanges.ps1; \
    dotnet restore "BridgeBeats.AppHost/BridgeBeats.AppHost.csproj" --locked-mode; \
    dotnet build "BridgeBeats.AppHost/BridgeBeats.AppHost.csproj" -c $BUILD_CONFIGURATION -o /app/build


# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release

# Publish the AppHost (orchestrator) - this includes all referenced projects
RUN dotnet publish "BridgeBeats.AppHost/BridgeBeats.AppHost.csproj" -c $BUILD_CONFIGURATION -o /app/publish/apphost

# Also publish individual projects for direct execution by Aspire
RUN dotnet publish "BridgeBeats.Web/BridgeBeats.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish/web
RUN dotnet publish "BridgeBeats.Worker.Spotify/BridgeBeats.Worker.Spotify.csproj" -c $BUILD_CONFIGURATION -o /app/publish/worker-spotify
RUN dotnet publish "BridgeBeats.Worker.AppleMusic/BridgeBeats.Worker.AppleMusic.csproj" -c $BUILD_CONFIGURATION -o /app/publish/worker-applemusic
RUN dotnet publish "BridgeBeats.Worker.Tidal/BridgeBeats.Worker.Tidal.csproj" -c $BUILD_CONFIGURATION -o /app/publish/worker-tidal

# Add startup script
COPY ./containers/entrypoint.sh /app/publish/entrypoint.sh
RUN chmod +x /app/publish/entrypoint.sh

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

# Create necessary directories with proper permissions
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app/data

# Copy application files
WORKDIR /app
COPY --from=publish /app/publish .

# Expose ports
# 10000: BridgeBeats main web application
# 15000-15099: Internal worker ports (managed by Aspire)
EXPOSE 10000
EXPOSE 15000-15099

# Set the AppHost as the entrypoint via the startup script
ENTRYPOINT ["/app/entrypoint.sh"]
