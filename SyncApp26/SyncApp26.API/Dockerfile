# syntax=docker/dockerfile:1
# Build context is the solution folder (SyncApp26/), not this directory alone — the API project
# references sibling projects (Application, Domain, Infrastructure, Shared) that must be present
# for `dotnet restore`/`publish` to resolve them.

########################
# Build stage
########################
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, from just the .csproj files, so this layer is cached across source-only changes.
COPY SyncApp26.API/SyncApp26.API.csproj SyncApp26.API/
COPY SyncApp26.Application/SyncApp26.Application.csproj SyncApp26.Application/
COPY SyncApp26.Domain/SyncApp26.Domain.csproj SyncApp26.Domain/
COPY SyncApp26.Infrastructure/SyncApp26.Infrastructure.csproj SyncApp26.Infrastructure/
COPY SyncApp26.Shared/SyncApp26.Shared.csproj SyncApp26.Shared/

RUN dotnet restore SyncApp26.API/SyncApp26.API.csproj

# Now bring in the rest of the source (Tests and Client are excluded via .dockerignore) and publish.
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

# curl is needed for the HEALTHCHECK below; the aspnet base image doesn't ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Pre-create the SQLite data dir and log dirs with the right ownership before dropping privileges —
# EF creates the .db file itself but never its parent directory, and a named volume mounted here
# inherits whatever the image already has at this path.
RUN mkdir -p /app/data /app/logs/errors \
    && chown -R app:app /app

COPY --from=build /app/publish .
COPY --from=build /src/VERSION /VERSION
# Baked-in config is the safe, secret-free template — real values (JWT secret, SMTP, OAuth, HMAC
# key) are injected at `docker compose up` time via environment variables, never into the image.
COPY SyncApp26.API/appsettings.example.json ./appsettings.json

RUN chown app:app ./appsettings.json

USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -f http://localhost:8080/api/version || exit 1

ENTRYPOINT ["dotnet", "SyncApp26.API.dll"]
