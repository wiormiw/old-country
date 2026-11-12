# old-country

A personal music player: your offline library plus YouTube Music, in one place.

- **Library**: scans your music folders (FLAC, MP3, AAC/ALAC, Opus, Vorbis, WAV), reads tags, and streams files with seeking.
- **Playlists**: ordered, editable, duplicates allowed.
- **YouTube Music**: search, play, download for offline, and import playlists or your liked songs.

| Part | Stack | Status |
|---|---|---|
| [`backend/`](backend/) | .NET 11 / C# 15, ASP.NET Core minimal APIs, EF Core 11, PostgreSQL 18, Aspire | Working |
| `desktop/` | Tauri v2 + Leptos (Rust/WASM) | Not started |

The backend runs on a home server; the desktop app talks to it over the LAN.

## Running the backend

**Prerequisites**

- .NET 11 SDK (`dotnet --list-sdks` shows `11.0.x`)
- Docker (Postgres for development and integration tests)
- [yt-dlp](https://github.com/yt-dlp/yt-dlp) and ffmpeg (YouTube Music features)

**Start everything** (Postgres container, the API, and the Aspire dashboard for logs and traces):

```bash
cd backend
dotnet run --project src/OldCountry.AppHost
```

Migrations run on startup in Development. The API's OpenAPI document is at `/openapi/v1.json` and an interactive UI at `/scalar`. `src/OldCountry.Api/OldCountry.Api.http` has a ready-made request for every endpoint.

**Configuration** (appsettings, environment variables like `Library__Roots__0`, or user secrets):

| Setting | Default | Meaning |
|---|---|---|
| `ConnectionStrings:oldcountry` | set by Aspire | PostgreSQL connection |
| `Library:Roots` | `[]` | Absolute music folders to scan and serve. Empty disables scanning. |
| `Library:RescanInterval` | `06:00:00` | Scheduled rescan; `00:00:00` turns it off |
| `Library:ScanOnStartup` | `true` | Scan when the server starts |
| `YouTubeMusic:YtDlpPath` | `yt-dlp` | yt-dlp executable |
| `YouTubeMusic:CookiesFile` | none | Browser-exported `cookies.txt`; needed for private playlists and liked songs |
| `YouTubeMusic:DownloadFolder` | `<first root>/YouTube Music` | Must be inside a library folder |
| `YouTubeMusic:AudioFormat` | `opus` | `opus`, `m4a`, `mp3` or `flac` |
| `YouTubeMusic:StreamCacheFolder` | system temp | Cache for YouTube audio being played |
| `YouTubeMusic:StreamCacheMaxMegabytes` | `2048` | Least recently played files are evicted first |

Bad configuration stops the server at startup with a message naming the setting.

## API at a glance

| Area | Endpoints |
|---|---|
| Tracks | `POST /api/tracks`, `GET /api/tracks`, `GET /api/tracks/{id}`, `GET /api/tracks/{id}/audio` |
| Library scans | `POST /api/tracks/scans`, `GET /api/tracks/scans/latest` |
| Playlists | `POST/GET /api/playlists`, `GET/PUT/DELETE /api/playlists/{id}`, `POST /api/playlists/{id}/tracks`, `DELETE /api/playlists/{id}/tracks/{position}`, `POST /api/playlists/{id}/tracks/move` |
| YouTube Music | `GET /api/youtube-music/search`, `GET /api/youtube-music/videos/{videoId}/audio`, `POST/GET /api/youtube-music/downloads`, `POST /api/youtube-music/imports` |

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details with a stable `code` field (`track.not_found`, `validation`, `youtube.failed`, ...).

## Backend design

```
backend/
  src/
    OldCountry.Domain/            entities, value objects, rules; references nothing
    OldCountry.Api/
      Features/<Feature>/<UseCase>/   one file per use case: request, validator, handler, endpoint
      Common/                         messaging, decorators, validation, errors, shared library helpers
      Persistence/                    DbContext, conventions, configurations, migrations
    OldCountry.ServiceDefaults/   OpenTelemetry, health checks, resilient HttpClients
    OldCountry.AppHost/           Aspire orchestration for development
  tests/                          Domain, Api (unit + integration), Architecture
```

- **Hybrid architecture.** Only the Domain is a "clean" layer. Everything else is vertical feature slices in one project; features never reference each other (enforced by architecture tests) and share only through Domain and `Common/`.
- **Explicit DI, no mediator.** Each feature lists its registrations (`TracksFeature.AddServices`), and `FeatureRegistry` lists every feature. Handlers are wrapped in hand-composed decorators (logging → transaction → handler) in `Common/Messaging/HandlerRegistration.cs`. No MediatR, AutoMapper or assembly scanning.
- **Results as C# 15 unions.** A use case returns e.g. `union GetTrackResult(TrackResponse, NotFoundError)`; endpoints `switch` over it exhaustively, so a new outcome won't compile until it is handled. Exceptions are for bugs.
- **Validation** with FluentValidation: one validator per request, run by an endpoint filter (`.Validate<T>()`) before the handler. The domain still enforces its own invariants.
- **Value objects are records**: strongly-typed ids (`readonly record struct`) via model-wide converters, `LocalFile`/`AudioQuality` as EF Core complex types, playlist entries as a JSON complex collection. snake_case names throughout.
- **Logging** only through source-generated `[LoggerMessage]` methods; CA1848/CA2254/CA1727/CA1873 are build errors.
- **Security**: audio is served only from inside the configured library folders (symlinks resolved), whatever path a track claims.

### YouTube Music notes

Everything goes through yt-dlp, run as a child process with a timeout. Playing a YouTube track fetches its audio into a size-capped cache and serves the file, rather than relaying YouTube's stream URLs: those are bound to the resolving IP, reject the HTTP `Range` header, and for some clients are refused unless fetched exactly as yt-dlp does (tested Nov 2026). Downloading from YouTube may be against its Terms of Service; this is a personal tool.

## Tests

```bash
cd backend
dotnet test --solution OldCountry.slnx                 # unit, architecture, and (with Docker) integration tests
OLDCOUNTRY_LIVE_YTDLP=1 dotnet test --solution OldCountry.slnx   # also run against real YouTube with the installed yt-dlp
```

Integration tests start a throwaway PostgreSQL with Testcontainers and are skipped when Docker isn't running. They use a fake yt-dlp; the live tests are the check that real yt-dlp output still parses.

## Notes

- EF Core is pinned to `11.0.0-rc.3` because the Npgsql provider for EF Core 11 is still a release candidate and requires that exact build. Bump `EfCoreVersion` in `backend/Directory.Packages.props` together with Npgsql when it ships GA.
- `dotnet tool restore` installs the matching `dotnet-ef` for migrations: `dotnet ef migrations add <Name> --project src/OldCountry.Api --output-dir Persistence/Migrations`.
