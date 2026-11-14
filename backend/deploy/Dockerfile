# The old-country API as a container: the published app plus what its YouTube Music features run
# (yt-dlp, Node.js as the JavaScript runtime yt-dlp needs for YouTube, and ffmpeg for audio conversion).
# Node rather than Deno, yt-dlp's default: Deno's development ends in 2027 (YouTubeMusic:JsRuntime).
# Build context is backend/:  docker build -f deploy/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
# Project files first, so the restore layer is reused until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/OldCountry.Domain/OldCountry.Domain.csproj src/OldCountry.Domain/
COPY src/OldCountry.Application/OldCountry.Application.csproj src/OldCountry.Application/
COPY src/OldCountry.Infrastructure/OldCountry.Infrastructure.csproj src/OldCountry.Infrastructure/
COPY src/OldCountry.ServiceDefaults/OldCountry.ServiceDefaults.csproj src/OldCountry.ServiceDefaults/
COPY src/OldCountry.Api/OldCountry.Api.csproj src/OldCountry.Api/
RUN dotnet restore src/OldCountry.Api/OldCountry.Api.csproj
COPY src/ src/
RUN dotnet publish src/OldCountry.Api/OldCountry.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg nodejs curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && node --version

# yt-dlp breaks whenever YouTube changes; bump YTDLP_VERSION (deploy/.env) and rebuild to update.
ARG YTDLP_VERSION=2026.10.26
RUN set -eux; \
    arch="$(dpkg --print-architecture)"; \
    case "$arch" in amd64) file=yt-dlp_linux ;; arm64) file=yt-dlp_linux_aarch64 ;; *) echo "unsupported: $arch"; exit 1 ;; esac; \
    curl -fsSL -o /usr/local/bin/yt-dlp "https://github.com/yt-dlp/yt-dlp/releases/download/${YTDLP_VERSION}/${file}"; \
    chmod +x /usr/local/bin/yt-dlp; \
    yt-dlp --version

# The container may run as any user id (compose sets your own, so files in the music folder are
# yours); point every cache at /tmp, which any user can write.
ENV HOME=/tmp XDG_CACHE_HOME=/tmp/.cache YouTubeMusic__JsRuntime=node

WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s CMD curl -fsS http://localhost:8080/alive || exit 1
ENTRYPOINT ["dotnet", "OldCountry.Api.dll"]
