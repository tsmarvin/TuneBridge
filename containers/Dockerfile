# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1aacc8154bc3071349907dae26849df301188be1a2e1f4560b903fb6275e481a AS base


# This stage is used to build and publish all projects
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:25d14b400b75fa4e89d5bd4487a92a604a4e409ab65becb91821e7dc4ac7f81f AS build
ARG TARGETARCH
WORKDIR /build

# Copy source files (projects directly in /build/) and build scripts (containers/ as sibling)
COPY src/ /build/
COPY containers/ /build/containers/

# Build and publish all projects using the standardized script
# The script handles lock file regeneration and validation for platform-specific packages
RUN DOTNET_RID="linux-x64"; \
    if [ "$TARGETARCH" = "arm64" ]; then DOTNET_RID="linux-arm64"; fi; \
    pwsh /build/containers/Build-Publish.ps1 -Runtime $DOTNET_RID

# Copy Aspire DCP and Dashboard tools from NuGet cache
RUN DOTNET_RID="linux-x64"; \
    if [ "$TARGETARCH" = "arm64" ]; then DOTNET_RID="linux-arm64"; fi; \
    mkdir -p /build/publish/tools/dcp && \
    mkdir -p /build/publish/tools/dashboard && \
    cp -r /root/.nuget/packages/aspire.hosting.orchestration.${DOTNET_RID}/13.1.0/tools/* /build/publish/tools/dcp/ && \
    cp -r /root/.nuget/packages/aspire.dashboard.sdk.${DOTNET_RID}/13.1.0/tools/* /build/publish/tools/dashboard/ && \
    chmod +x /build/publish/tools/dcp/dcp


# Final runtime stage
FROM base AS final

# Create necessary directories with proper permissions (as root, before switching to app user)
RUN mkdir -p /app/data && \
    mkdir -p /src

# Copy published application (publish/src/* -> /src/*, publish/entrypoint.sh -> /entrypoint.sh)
COPY --from=build /build/publish/src /src
COPY --from=build /build/publish/entrypoint.sh /entrypoint.sh

# Copy Aspire DCP and Dashboard tools
COPY --from=build /build/publish/tools/dcp /app/tools/dcp
COPY --from=build /build/publish/tools/dashboard /app/tools/dashboard

RUN chown -R $APP_UID:$APP_UID /src && \
    chown -R $APP_UID:$APP_UID /app/ && \
    chown $APP_UID:$APP_UID /entrypoint.sh && \
    chmod +x /entrypoint.sh && \
    chmod +x /app/tools/dcp/dcp

USER $APP_UID

# Set NUGET_PACKAGES so Aspire finds the dashboard SDK
ENV ASPNETCORE_URLS=http://+:18888
ENV ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
ENV ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://+:18889
ENV ASPIRE_DASHBOARD_TELEMETRY_OPTOUT=true
ENV ASPIRE_DASHBOARD_AI_DISABLED=true

# Expose ports
# 10000: BridgeBeats main web application
# 18888: Aspire Dashboard web UI
# 18889: Aspire Dashboard OTLP endpoint
# 15000-15099: Internal worker ports (managed by Aspire)
EXPOSE 10000
EXPOSE 18888
EXPOSE 18889
EXPOSE 15000-15099

# Set the AppHost as the entrypoint via the startup script
ENTRYPOINT ["/entrypoint.sh"]
