#!/usr/bin/env bash
# Runs only the API, without Aspire, against the same database the AppHost uses (so your library and
# playlists are all there). It:
#   1. starts the Postgres container the AppHost created (persistent, localhost:55432),
#   2. reads the Postgres password and MusicFolder from the AppHost's user secrets,
#   3. runs the API on http://localhost:5000 in Development (Scalar UI at /scalar, logs in this terminal).
# Run the AppHost once before this, so the container and secrets exist. Ctrl+C stops the API; the
# database keeps running (docker stop it, or leave it for next time).
set -euo pipefail
cd "$(dirname "$0")"

# The .NET 11 SDK may live in ~/dotnet instead of on the PATH.
if ! dotnet --list-sdks 2>/dev/null | grep -q '^11\.' && [ -x "$HOME/dotnet/dotnet" ]; then
  export DOTNET_ROOT="$HOME/dotnet" PATH="$HOME/dotnet:$PATH"
fi

secrets=$(dotnet user-secrets list --project src/OldCountry.AppHost)
password=$(sed -n 's/^Parameters:postgres-password = //p' <<<"$secrets")
music=$(sed -n 's/^MusicFolder = //p' <<<"$secrets")
if [ -z "$password" ]; then
  echo "No Postgres password in the AppHost's user secrets. Set one:" >&2
  echo "  dotnet user-secrets set Parameters:postgres-password '<choose one>' --project src/OldCountry.AppHost" >&2
  exit 1
fi

# Aspire names the container postgres-<hash>; its data volume is oldcountry.apphost-<hash>-postgres-data.
container=$(docker ps -a --no-trunc --format '{{.Names}} {{.Mounts}}' | awk '/oldcountry\.apphost-[^ ]*postgres-data/ { print $1; exit }')
if [ -z "$container" ]; then
  echo "The AppHost's Postgres container doesn't exist yet. Run the AppHost once:" >&2
  echo "  dotnet run --project src/OldCountry.AppHost" >&2
  exit 1
fi
docker start "$container" >/dev/null
until docker exec "$container" pg_isready -U postgres -q; do sleep 0.5; done
echo "Postgres: $container on localhost:55432"

export ConnectionStrings__oldcountry="Host=localhost;Port=55432;Database=oldcountry;Username=postgres;Password=$password"
if [ -n "$music" ]; then
  export Library__Roots__0="$music"
  echo "Music folder: $music"
fi

echo "API: http://localhost:5000  (Scalar: http://localhost:5000/scalar)"
exec dotnet run --project src/OldCountry.Api --launch-profile http
