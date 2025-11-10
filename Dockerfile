# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY src/ .
RUN dotnet restore "TuneBridge.csproj"
RUN dotnet build "TuneBridge.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TuneBridge.csproj" -c $BUILD_CONFIGURATION -o /app/publish -r "linux-arm64"

# Add startup script and Caddyfile
COPY ./entrypoint.sh /app/publish/entrypoint.sh
COPY ./Caddyfile /app/publish/Caddyfile
RUN chmod +x /app/publish/entrypoint.sh

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

# Switch to root to install Caddy and create directories
USER root

# Install Caddy web server
# Using official Caddy installation script for ARM64
RUN apt-get update && apt-get install -y \
    debian-keyring \
    debian-archive-keyring \
    apt-transport-https \
    curl \
    && curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg \
    && curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | tee /etc/apt/sources.list.d/caddy-stable.list \
    && apt-get update \
    && apt-get install -y caddy \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Create necessary directories with proper permissions
RUN mkdir -p /app/data /data/caddy /config/caddy /var/log/caddy /etc/caddy \
    && chown -R $APP_UID:$APP_UID /app/data /data/caddy /config/caddy /var/log/caddy /etc/caddy

# Copy application files
WORKDIR /app
COPY --from=publish /app/publish .

# Copy Caddyfile to Caddy's config directory
RUN cp /app/Caddyfile /etc/caddy/Caddyfile && chown $APP_UID:$APP_UID /etc/caddy/Caddyfile

# Switch back to non-root user
USER $APP_UID

# Expose ports
# 10000: TuneBridge application (internal)
# 80: HTTP (Caddy will redirect to HTTPS)
# 443: HTTPS (Caddy with automatic TLS)
EXPOSE 10000 80 443

ENTRYPOINT ["/app/entrypoint.sh"]
