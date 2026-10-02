# syntax=docker/dockerfile:1
#
# BusinessCard.Api container image.
# Build context is the REPOSITORY ROOT: the API embeds inference/contract/* (prompt + schema).
#
#   docker build -f backend/Dockerfile -t businesscard-api .
#
# Pin both base images by digest in production (docker inspect --format '{{index .RepoDigests 0}}' <image>).

ARG DOTNET_VERSION=8.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore first, from project files only, so this layer is cached until dependencies change.
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/BusinessCard.Domain/BusinessCard.Domain.csproj backend/src/BusinessCard.Domain/
COPY backend/src/BusinessCard.Application/BusinessCard.Application.csproj backend/src/BusinessCard.Application/
COPY backend/src/BusinessCard.Infrastructure/BusinessCard.Infrastructure.csproj backend/src/BusinessCard.Infrastructure/
COPY backend/src/BusinessCard.Api/BusinessCard.Api.csproj backend/src/BusinessCard.Api/
RUN dotnet restore backend/src/BusinessCard.Api/BusinessCard.Api.csproj

COPY backend/src backend/src
COPY inference/contract inference/contract
RUN dotnet publish backend/src/BusinessCard.Api/BusinessCard.Api.csproj \
      --configuration Release --no-restore --output /app /p:UseAppHost=false

# Debian-based runtime on purpose: Turkish casing rules need ICU, which Alpine images lack.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
COPY --from=build /app .

# Non-root user provided by the official .NET images.
USER $APP_UID
EXPOSE 8080

# No HEALTHCHECK: the runtime image has no curl. Orchestrators probe /health (liveness)
# and /health/ready (readiness; the LLM reports Degraded, never Unhealthy).
ENTRYPOINT ["dotnet", "BusinessCard.Api.dll"]
