# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["TuneBridge.csproj", "TuneBridge/"]
RUN dotnet restore "TuneBridge/TuneBridge.csproj"
COPY . .
WORKDIR "/src/TuneBridge"
RUN dotnet build "./TuneBridge.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./TuneBridge.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -r "linux-arm64"

# Add startup script
COPY ./entrypoint.sh /app/publish/entrypoint.sh
RUN chmod +x /app/publish/entrypoint.sh

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 10000
ENTRYPOINT ["/app/entrypoint.sh"]
