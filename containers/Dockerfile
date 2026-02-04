# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1aacc8154bc3071349907dae26849df301188be1a2e1f4560b903fb6275e481a AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build and publish all projects
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:25d14b400b75fa4e89d5bd4487a92a604a4e409ab65becb91821e7dc4ac7f81f AS build
ARG TARGETARCH

WORKDIR /src
COPY src/ .
COPY containers/ containers/

# Build and publish all projects using the standardized script
# The script handles lock file regeneration and validation for platform-specific packages
RUN DOTNET_RID="linux-x64"; \
    if [ "$TARGETARCH" = "arm64" ]; then DOTNET_RID="linux-arm64"; fi; \
    pwsh containers/Build-Publish.ps1 -Runtime $DOTNET_RID; \
    chmod +x publish/entrypoint.sh

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

# Create necessary directories with proper permissions
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app/data

# Copy application files
WORKDIR /app
COPY --from=build /src/publish .

# Expose ports
# 10000: BridgeBeats main web application
# 15000-15099: Internal worker ports (managed by Aspire)
EXPOSE 10000
EXPOSE 15000-15099

# Set the AppHost as the entrypoint via the startup script
ENTRYPOINT ["/app/entrypoint.sh"]
