# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy all project files for restore
COPY src/TuneBridge/TuneBridge.csproj src/TuneBridge/
COPY src/TuneBridge.AppHost/TuneBridge.AppHost.csproj src/TuneBridge.AppHost/

# Restore dependencies
RUN dotnet restore "src/TuneBridge.AppHost/TuneBridge.AppHost.csproj"
RUN dotnet restore "src/TuneBridge/TuneBridge.csproj"

# Copy all source files
COPY src/TuneBridge/ src/TuneBridge/
COPY src/TuneBridge.AppHost/ src/TuneBridge.AppHost/

# Build projects
RUN dotnet build "src/TuneBridge/TuneBridge.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN dotnet build "src/TuneBridge.AppHost/TuneBridge.AppHost.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish both projects to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release

# Publish TuneBridge to /app/publish (main application)
RUN dotnet publish "src/TuneBridge/TuneBridge.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# Publish AppHost to the same directory so it can find TuneBridge.dll
RUN dotnet publish "src/TuneBridge.AppHost/TuneBridge.AppHost.csproj" -c $BUILD_CONFIGURATION -o /app/publish

# Add startup script
COPY ./entrypoint.sh /app/publish/entrypoint.sh
RUN chmod +x /app/publish/entrypoint.sh

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

# Create necessary directories with proper permissions
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app/data

# Copy application files
WORKDIR /app
COPY --from=publish /app/publish .

# Expose ports
# 10000: TuneBridge application (internal)
EXPOSE 10000

ENTRYPOINT ["/app/entrypoint.sh"]
