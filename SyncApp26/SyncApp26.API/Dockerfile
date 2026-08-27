# syntax=docker/dockerfile:1
# Build context: SyncApp26/ (sibling projects needed for restore).

########################
# Build stage
########################
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore separately for layer caching.
COPY SyncApp26.API/SyncApp26.API.csproj SyncApp26.API/
COPY SyncApp26.Application/SyncApp26.Application.csproj SyncApp26.Application/
COPY SyncApp26.Domain/SyncApp26.Domain.csproj SyncApp26.Domain/
COPY SyncApp26.Infrastructure/SyncApp26.Infrastructure.csproj SyncApp26.Infrastructure/
COPY SyncApp26.Shared/SyncApp26.Shared.csproj SyncApp26.Shared/

RUN dotnet restore SyncApp26.API/SyncApp26.API.csproj

# Tests/Client excluded via .dockerignore.
COPY SyncApp26.API/ SyncApp26.API/
COPY SyncApp26.Application/ SyncApp26.Application/
COPY SyncApp26.Domain/ SyncApp26.Domain/
COPY SyncApp26.Infrastructure/ SyncApp26.Infrastructure/
COPY SyncApp26.Shared/ SyncApp26.Shared/
COPY VERSION VERSION

RUN dotnet publish SyncApp26.API/SyncApp26.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:UseAppHost=false

########################
# Runtime stage
########################
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl needed for HEALTHCHECK below.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# EF creates the .db file but not its parent dir.
RUN mkdir -p /app/data /app/logs/errors \
    && chown -R app:app /app

COPY --from=build /app/publish .
COPY --from=build /src/VERSION /VERSION
# Secret-free template; real values are injected via env vars at runtime.
COPY SyncApp26.API/appsettings.example.json ./appsettings.json
# Point the default connection string at the volume mount, not the dev-relative path.
RUN sed -i 's#Data Source=../SyncApp26.Infrastructure/SyncApp26.db;Mode=ReadWrite#Data Source=/app/data/SyncApp26.db;Mode=ReadWriteCreate#' ./appsettings.json

RUN chown app:app ./appsettings.json

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -f http://localhost:8080/api/version || exit 1

ENTRYPOINT ["dotnet", "SyncApp26.API.dll"]
