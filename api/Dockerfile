# ── Stage 1: build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer-cache friendly)
COPY VinLoggen.Api.csproj .
RUN dotnet restore

# Build & publish
COPY . .
RUN dotnet publish VinLoggen.Api.csproj \
        --configuration Release \
        --output /app/publish \
        --no-restore

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Run as non-root for security
RUN apt-get update && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && groupadd --gid 1001 appgroup \
 && useradd  --uid 1001 --gid 1001 --no-create-home appuser
USER appuser

COPY --from=build --chown=appuser:appgroup /app/publish .

# Default port for docker-compose. Render overrides this via $PORT at runtime.
# Program.cs maps $PORT → WebHost.UseUrls when set.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VinLoggen.Api.dll"]
