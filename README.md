# MyVirtualLibrary
Personal book tracker backed by OpenLibrary. ISBN in → edition/work/author/cover cached locally; users manage their own "books", flag them as WantToRead / Owned / Read, and lay them out on virtual shelves.

## Solution layout
```
MyVirtualLibrary/
├── VirtualLibrary.Shared/   # DTOs, enums (netstandard2.1, used by API + client)
├── VirtualLibrary.Api/      # ASP.NET Core 10 Web API + EF Core + Identity
│   ├── Migrations/          # EF Core migrations
│   └── seed-data/           # mock-isbn-cache.json written on first dev boot
├── VirtualLibrary.Client/   # Uno Platform app (net10.0-browserwasm, net10.0-android, net10.0-maccatalyst)
├── docs/
│   └── er-diagram.md        # Mermaid ER diagram of the schema
├── docker-compose.yml       # api (Debug build) + postgres:16
└── VirtualLibrary.Api/Dockerfile
```
Schema reference: [`docs/er-diagram.md`](docs/er-diagram.md).

## Prerequisites
- **.NET 10 SDK** — `dotnet --version` should report `10.x`.
- One of:
  - **Docker Desktop** (preferred — one command brings up API + DB), or
  - **PostgreSQL 16 / 17** running locally on `localhost:5432` with a superuser.
- Optional:
  - `dotnet-ef` global tool (for generating migrations): `dotnet tool install -g dotnet-ef`.
  - **Android workload** if you want to build the Android head: `dotnet workload install android`.
  - **macOS Catalyst workload** if you want to build the Mac desktop head: `dotnet workload install maccatalyst` (macOS only; Xcode required for signing on real hardware).

## First-time setup
From the repo root:
```bash
dotnet restore
dotnet build VirtualLibrary.Api
dotnet build VirtualLibrary.Client -f net10.0-browserwasm
```
The first `build` pulls the Uno SDK and the net10 targeting packs; expect ~5 min cold.

## Running the stack
### Option A — Docker Compose (recommended)
```bash
docker compose up --build
```
What happens:
- `db` — `postgres:16` with named volume `virtuallibrary-pgdata`, exposed on host port **5433** (avoids colliding with a local Postgres on 5432).
- `api` — the ASP.NET Core service built in **Debug** configuration (so `#if DEBUG` blocks such as `DevAuthController` and `MockDataSeeder` are active), exposed on `http://localhost:5179`.
- On first boot the API runs `Database.Migrate()`, seeds the SuperAdmin account, seeds the five dev personas, and runs `MockDataSeeder` (see [Dev seeding](#dev-seeding) below).

Stop with `Ctrl+C`; remove with `docker compose down` (keeps the volume) or `docker compose down -v` (wipes data and forces a clean migration run on next boot).

### Option B — native Postgres + `dotnet run`
Useful when Docker isn't running. Works with Homebrew's `postgresql@16` or `postgresql@17`.
1. Create the database and role the API expects:
    ```bash
    psql -U $(whoami) -d postgres -c "CREATE DATABASE virtuallibrary;"
    psql -U $(whoami) -d postgres -c "ALTER USER postgres WITH PASSWORD 'postgres';"
    psql -U $(whoami) -d virtuallibrary -c \
        "GRANT ALL ON SCHEMA public TO postgres;
         ALTER SCHEMA public OWNER TO postgres;
         ALTER DATABASE virtuallibrary OWNER TO postgres;"
    ```
2. Run the API pointed at localhost:
    ```bash
    ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=virtuallibrary;Username=postgres;Password=postgres" \
    ASPNETCORE_URLS="http://localhost:5179" \
    ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --project VirtualLibrary.Api --no-launch-profile
    ```
    Health probe: `curl http://localhost:5179/health` → `{"status":"healthy"}`.

### Running the Uno client
The client is a single Uno project targeting WASM, Android, and Mac Catalyst.
```bash
# Web (WASM) — opens http://localhost:5000 with hot reload
dotnet run --project VirtualLibrary.Client -f net10.0-browserwasm -c Debug

# Android (requires the Android workload + running emulator)
dotnet build VirtualLibrary.Client -f net10.0-android -c Debug

# Mac Catalyst (macOS host only; requires the maccatalyst workload + Xcode for signing)
dotnet build VirtualLibrary.Client -f net10.0-maccatalyst -c Debug
dotnet run   --project VirtualLibrary.Client -f net10.0-maccatalyst -c Debug
```
The client's `ApiClient` resolves the API base URL per-platform (`Services/ApiClient.cs`):
- **WASM Debug** → `http://localhost:5179` directly (the Uno dev server on 5000 and the API on 5179 are different origins; CORS allows both in dev).
- **WASM Release** → same origin via `window.location.origin` (nginx reverse-proxies `/api/*` to the backend in prod).
- **Android emulator** → `http://10.0.2.2:5179`.
- **Desktop / Mac Catalyst** → `http://localhost:5179`.

## Dev seeding
On every Debug boot against a fresh database, the API automatically seeds:

**Dev personas** (for the dev-login panel — no password needed):

| Email | Role | Status |
|---|---|---|
| `superadmin@dev.local` | SuperAdmin | Active |
| `admin@dev.local` | Admin | Active |
| `member@dev.local` | User | Active |
| `pending@dev.local` | User | PendingApproval |
| `suspended@dev.local` | User | Suspended |

**Mock library users** (`MockDataSeeder`) — 10 realistic member accounts (`emma.lindqvist@example.com` … `lucas.gustafsson@example.com`), all Active. On first run the seeder queries the OpenLibrary Search API for 6 topics (fantasy, sci-fi, mystery, history, biography, literary fiction), collects up to 10 ISBN-13s per topic, and writes the results to `VirtualLibrary.Api/seed-data/mock-isbn-cache.json`. Subsequent runs (e.g. after a DB wipe) load from that file instead of hitting the network. Each user receives a "popular" pool of books shared across all accounts plus a randomly-selected niche set, so the admin stats view shows realistic multi-user overlap.

All seeding is idempotent — safe to call on every restart.

## Default credentials
The SuperAdmin account seeded in all `Development` environments:
- Email: `admin@virtuallibrary.local`
- Password: `Admin123!`
- Role: `SuperAdmin`, Status: `Active`

Use this on the **Sign in with password** form. The dev personas and mock users are intended for use with the **Dev Login** panel (Debug builds only) — see `POST /api/auth/dev-login` below.

## Useful commands
```bash
# Regenerate EF Core migration
dotnet ef migrations add <Name> --project VirtualLibrary.Api --output-dir Migrations

# Apply pending migrations manually (normally auto-applied in dev)
dotnet ef database update --project VirtualLibrary.Api

# Wipe Docker volume and rebuild from scratch (required after a migration squash or schema conflict)
docker compose down -v --remove-orphans && docker compose up --build

# Drop & recreate the database (native setup)
psql -U postgres -d postgres -c "DROP DATABASE IF EXISTS virtuallibrary; CREATE DATABASE virtuallibrary;"

# Re-run mock seeding after a DB wipe (delete cache to force a fresh OL search)
rm VirtualLibrary.Api/seed-data/mock-isbn-cache.json

# Build everything
dotnet build VirtualLibrary.Shared
dotnet build VirtualLibrary.Api
dotnet build VirtualLibrary.Client -f net10.0-browserwasm
```

## API quick reference
All endpoints return JSON. All `/api/*` routes require `Authorization: Bearer <JWT>` unless noted.
- `GET  /health` (anonymous)
- `POST /api/auth/login/password`   — seeded admin / future local accounts
- `POST /api/auth/login`            — external IdToken exchange
- `GET  /api/auth/me`               — current profile
- `POST /api/auth/refresh`          — re-issue JWT with latest role/status
- `GET  /api/users[?status=…]`      — Admin / SuperAdmin
- `GET  /api/users/{id}`            — Admin / SuperAdmin
- `POST /api/users/{id}/approve`    — `{ "approved": true|false }`
- `POST /api/users/{id}/suspend`    — Admin / SuperAdmin
- `POST /api/users/{id}/reactivate` — Admin / SuperAdmin
- `POST /api/users/{id}/role`       — SuperAdmin (`{ "role": 0|1|2 }`)
- `DELETE /api/users/{id}`          — SuperAdmin
- `POST /api/lookup/{isbn}`         — OpenLibrary lookup, caches to DB
- `GET  /api/books[?status=…][?isOwned=…]` — current user's books; filter by reading status and/or ownership
- `POST /api/books`                 — `{ "isbn": "...", "status": 0|1, "isOwned": true|false }` (defaults: WantToRead + owned)
- `GET  /api/books/{id}`            — single user-book
- `PATCH /api/books/{id}`           — `{ "status"?, "isOwned"?, "rating"?, "notes"? }`
- `DELETE /api/books/{id}`
- `GET  /api/books/{id}/reads`      — list all read records for a book
- `POST /api/books/{id}/reads`      — log a reading: `{ "dateRead"?: "ISO-8601", "notes"?: "…" }` (dateRead defaults to now)
- `DELETE /api/books/{id}/reads/{recordId}` — remove a read record
- `GET  /api/shelves/default`         — load-or-create default shelf (unplaced owned books merged in)
- `PUT  /api/shelves/{id}/placements` — `{ "userBookIds": ["<uuid>", …] }` replaces all placements in slot order
- `POST /api/auth/exchange`           — `{ "provider", "code", "codeVerifier", "redirectUri" }` PKCE code exchange (preferred over `/login`)
- `POST /api/import`                  — bulk-import up to 500 ISBNs; fetches/refreshes metadata from OpenLibrary and adds books to the calling user's library. Body: `{ "isbns": ["…"], "defaultStatus": 0|1|2, "defaultIsOwned": true|false }`
- `GET  /api/stats`                   — library-wide statistics (Admin / SuperAdmin only): catalogue counts, user-book aggregates, top authors, top subjects, active member count
- `POST /api/auth/dev-login`          — **Debug builds only**; body `{ "persona": "<name>" }`. Issues a real JWT for a named test persona without credentials. Personas: `superadmin`, `admin`, `member`, `pending`, `suspended`. Returns 404 in non-Development environments even if compiled as Debug.
- `GET  /api/bestseller`              — list of all available NYT bestseller list names (encoded slugs, e.g. `hardcover-fiction`). Requires `NytBooks:ApiKey`.
- `GET  /api/bestseller/{listName}`   — current NYT bestseller list for `listName`. Returns `BestsellerListDto` (list name, published date, ranked books with ISBN-13, cover URL, weeks on list). Cached 24 h.
- `GET  /api/trending[?period=weekly]` — trending works from Open Library. `period` is `daily` or `weekly` (default `weekly`). Returns `TrendingResultDto` with up to 20 works; `localWorkId` is populated if the work is already in your library. Cached 1 h. No API key required.

Enum reference: `BookStatus 0=WantToRead, 1=Read` (ownership is a separate `isOwned` bool, not an enum value); `UserRole 0=User, 1=Admin, 2=SuperAdmin`; `UserStatus 0=PendingApproval, 1=Active, 2=Rejected, 3=Suspended`.

## Discovery API keys

### NYT Books API
Register a free key at <https://developer.nytimes.com/> (instant approval, 1000 req/day).
```bash
cd VirtualLibrary.Api
dotnet user-secrets set "NytBooks:ApiKey" "<your-key>"
```
Or set `NytBooks__ApiKey` as an environment variable in production. Without a key the `/api/bestseller` endpoints return 404; `/api/trending` always works without one.

## OAuth setup
External sign-in (Google / Apple) requires credentials from each provider's developer console **and** public client IDs baked into the client app. No secrets are committed to the repo.

### Server-side secrets (API)
The API validates tokens using each provider's published public keys. It reads the audience / client ID from configuration:
```bash
# Local dev — user secrets (never committed)
cd VirtualLibrary.Api
dotnet user-secrets set "Auth:Google:ClientId"     "<web-app-client-id>.apps.googleusercontent.com"
dotnet user-secrets set "Auth:Google:ClientSecret" "<secret>"   # needed only for cookie-based flows
dotnet user-secrets set "Auth:Apple:ClientId"      "com.yourcompany.virtualibrary.web"
```
For production, supply the same keys as environment variables:
```
Auth__Google__ClientId=...
Auth__Apple__ClientId=...
```

### Google Cloud Console setup
1. Create a project at <https://console.cloud.google.com>.
2. Enable the **People API**.
3. **Credentials → Create → OAuth 2.0 Client ID (type: Web application)**.
   - Authorized JavaScript origins: `http://localhost:5000` (dev), `https://yourdomain.com` (prod).
   - Authorized redirect URIs: same origins + `/signin-google` if using server-side redirect.
4. Copy the Client ID (ends in `.apps.googleusercontent.com`) into `Auth:Google:ClientId` (server) and into `OAuthConfig.GoogleClientId` in `VirtualLibrary.Client/Services/OAuthConfig.cs` (WASM/Android).
5. **Optional — Android native**: Create a second Client ID (type: Android) with your app's SHA-1 fingerprint and package name `com.virtuallibrary.client`. Google Play Services uses this internally; the OIDC audience remains the **web** client ID.

### Apple Developer setup
1. Sign in at <https://developer.apple.com>.
2. **Certificates, IDs & Profiles → Identifiers → + → Services IDs**. Enable *Sign In with Apple*.
3. Add your domain and redirect URIs (must be HTTPS in production).
4. Create a **Sign In with Apple** key under **Keys**; download the `.p8` file.
5. Set `Auth:Apple:ClientId` (your Services ID, e.g. `com.yourcompany.virtualibrary.web`) and `Auth:Apple:TeamId` / `Auth:Apple:KeyId` / `Auth:Apple:PrivateKey` via user secrets or environment variables.
6. Copy the Services ID into `OAuthConfig.AppleClientId`.

### Client-side IDs
Paste the **public** client IDs (not secrets) into `VirtualLibrary.Client/Services/OAuthConfig.cs`:
```csharp
// __WASM__ block
public const string GoogleClientId = "123456789-xxxx.apps.googleusercontent.com";
public const string AppleClientId  = "com.yourcompany.virtualibrary.web";
```
These values appear in browser URLs and are safe to commit.

### Remaining work
- iOS native Sign In with Apple button via `AuthenticationServices.ASAuthorizationController` (requires adding `net10.0-ios` TFM).

## Barcode scanner (Android + Mac Catalyst)
The `ScanPage` resolves `VirtualLibrary.Client.Services.IIsbnScanner` via a tiny platform-conditional factory. On heads without a camera backend it falls back to `ManualIsbnScanner` (camera button disabled). Two live backends ship today:

- **Android** — `VirtualLibrary.Client.Platforms.Android.AndroidIsbnScanner` backed by `Plugin.Scanner.Uno 0.0.1` (ML Kit). `USE_PLUGIN_SCANNER_UNO` is defined unconditionally for the Android TFM in `VirtualLibrary.Client.csproj`, so the live camera path is active. `ScannerBootstrap` wires a minimal `ServiceCollection` with `Plugin.Scanner.Uno.Android.CurrentActivity` (the Uno-aware activity provider) + `AddScanner()`, then caches the resolved `IBarcodeScanner` for the app lifetime. Camera and flashlight permissions are declared in `VirtualLibrary.Client/Platforms/Android/AndroidManifest.xml`.
- **Mac Catalyst** — `VirtualLibrary.Client.Platforms.MacCatalyst.MacCatalystIsbnScanner`, a hand-rolled AVFoundation `UIViewController` that wraps `AVCaptureSession` + `AVCaptureMetadataOutput` targeting `EAN13` / `EAN8` symbologies. No third-party scanner dependency — the Mac Catalyst runtime ships AVFoundation, so the code lives alongside the head. `USE_AVFOUNDATION_SCANNER` is defined automatically for the Catalyst TFM. The scanner works with the built-in FaceTime HD camera, external USB webcams, and Continuity Camera (iPhone-as-webcam). `IsSupported` is evaluated dynamically — Macs with no attached camera fall back to manual entry instead of crashing.

Catalyst camera access requires two things to be bundled into the signed `.app`:

1. `NSCameraUsageDescription` in `VirtualLibrary.Client/Platforms/MacCatalyst/Info.plist` (the string shown in the system permission dialog).
2. The `com.apple.security.device.camera` entitlement in `VirtualLibrary.Client/Platforms/MacCatalyst/Entitlements.plist` — required because Catalyst apps run sandboxed by default.

Both are already wired via the `maccatalyst` PropertyGroup / ItemGroup in `VirtualLibrary.Client.csproj`; no further manifest edits are needed.

## Troubleshooting
- **`docker compose up` fails with `Name or service not known`** — Docker's internal DNS lost the `db` hostname. Run `docker compose down && docker compose up --build` to recreate the network.
- **`column X does not exist` at startup** — the EF Core migration history table contains entries for migrations that never actually ran DDL against the database (phantom migrations). Fix: wipe the volume so `InitialCreate` can run the full schema from scratch: `docker compose down -v --remove-orphans && docker compose up --build`. The three original migrations (`InitialCreate`, `AddReadRecordsAndIsOwned`, `AddOlLastModified`) have been squashed — `InitialCreate` now contains the complete schema; the later two are no-ops kept only so existing history entries don't break the runner.
- **API returns 500 on `/api/books`** — was caused by EF Core 9+ rejecting multi-collection includes in a single query. Fixed by enabling `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)` globally on the Npgsql options in `Program.cs`.
- **WASM dev-login returns 404** — the API was built in Release (strips `#if DEBUG` blocks). The `docker-compose.yml` passes `BUILD_CONFIGURATION=Debug` to the Dockerfile; make sure you rebuilt with `--build` after that change.
- **WASM API calls blocked by CORS** — in Debug the client calls `http://localhost:5179` from `http://localhost:5000`. Both origins are listed in `AllowedOrigins` in `docker-compose.yml` and `appsettings.Development.json`. If you see a CORS error, confirm the API was restarted after the `docker-compose.yml` change.
- **Port 5432 already in use** — the compose file binds PostgreSQL to host port **5433** to avoid clashing with a locally running Postgres instance. If 5433 is also taken, update the `ports` entry in `docker-compose.yml` and the registry in `.claude/port-registry.json`.
- **Mac Catalyst build requires Apple Silicon** — Xcode 26.x is ARM64-only; building `net10.0-maccatalyst` on an Intel Mac fails with `Bad CPU type in executable` from `xcodebuild`/`actool`. An M-series Mac is required.
- **Mac Catalyst build fails: "This version of .NET for MacCatalyst requires Xcode X.Y"** (Apple Silicon only) — the .NET MacCatalyst workload is ABI-tied to a specific Xcode version. Setting `ValidateXcodeVersion=false` bypasses the guard but the linker still fails with ICU undefined-symbol errors (e.g. `_u_errorName_77`) because the ICU version in the workload doesn't match what the installed Xcode ships.
  - **Root cause**: Xcode 26.4.x ships ICU 78; workload 26.2.10233 (built for Xcode 26.3) embeds ICU 77. Binary-incompatible; `ValidateXcodeVersion=false` cannot resolve this.
  - **Fix A**: install the required Xcode from [developer.apple.com/download/more](https://developer.apple.com/download/more) and point dotnet at it without changing the system default:
    ```bash
    DEVELOPER_DIR=/Applications/Xcode-26.3.app/Contents/Developer dotnet build VirtualLibrary.Client -f net10.0-maccatalyst
    ```
  - **Fix B**: run `sudo dotnet workload update` once Microsoft ships a workload targeting your Xcode version (see [aka.ms/xcode-requirement](https://aka.ms/xcode-requirement)).
- **`VirtualLibrary.Shared` fails with `IsExternalInit is not defined`** — the polyfill lives in `VirtualLibrary.Shared/Polyfills.cs`. Don't delete it; it's required because `netstandard2.1` predates C# 9 init-only setters.
- **API boots then dies with `nodename nor servname provided, or not known`** — the default connection string uses `Host=db` (Docker Compose name). For a native run, override `ConnectionStrings__DefaultConnection` as shown above.
- **WASM build warns `IL2026` on JSON methods** — fixed in this repo: `AppJsonContext` (source-generated `JsonSerializerContext`) covers all DTOs and all `ApiClient` call-sites use the trim-safe `JsonTypeInfo<T>` overloads. If you see new IL2026 warnings after adding a DTO, add a matching `[JsonSerializable]` entry to `VirtualLibrary.Client/Services/AppJsonContext.cs`.
- **Docker Desktop not running** — `docker compose up` fails with `Cannot connect to the Docker daemon`. Start Docker Desktop, or use Option B above.

## Implementation status
See `docs/er-diagram.md` for the data model. Plan progress:
- [x] Shared DTOs, enums, netstandard2.1 polyfill
- [x] AppDbContext + Identity + library tables + InitialCreate migration
- [x] ASP.NET Core API: Auth, Users, Books, Lookup controllers; SuperAdmin seeding
- [x] OpenLibrary client with DB + memory cache and rate limiting
- [x] Docker Compose + multi-stage API Dockerfile with `BUILD_CONFIGURATION` arg (Debug in compose, Release default)
- [x] Uno client pages: Login, PendingApproval, Scan, Library, BookDetail, Shelf, UserManagement
- [x] Android ISBN scanner — `IIsbnScanner` abstraction + `AndroidIsbnScanner` (live `Plugin.Scanner.Uno` path) + `AndroidManifest.xml` permissions + `ScannerBootstrap` DI wiring
- [x] Mac Catalyst ISBN scanner — `net10.0-maccatalyst` TFM + hand-rolled AVFoundation `MacCatalystIsbnScanner` (EAN-13/EAN-8 via `AVCaptureMetadataOutput`) + `Info.plist` / `Entitlements.plist` with `NSCameraUsageDescription` and `com.apple.security.device.camera`; supports built-in, external, and Continuity cameras, degrades to manual entry on camera-less Macs
- [x] Virtual shelf: drag/drop reorder (`ListView` `CanReorderItems`) + physical-dimension spine widths + `ShelvesController` (load-or-create default shelf, batch-replace placements)
- [x] Production OAuth wiring — `ExternalTokenValidatorFactory` (Google via `GoogleJsonWebSignature`, Apple via OIDC discovery + JWKS), `OAuthConfig` for client IDs, configurable via user secrets / env vars; implicit flow wired end-to-end (PKCE upgrade tracked in issue #5)
- [x] Trim-safe WASM — `AppJsonContext` source-generated `JsonSerializerContext` + all `ApiClient` call-sites use `JsonTypeInfo<T>` overloads; zero IL2026 warnings on Release WASM build
- [x] Read record tracking — `ReadRecord` entity (start/finish dates) linked to `UserBook`; `IsOwned` flag added to `UserBook`; all schema changes squashed into `InitialCreate` (later migrations are no-ops)
- [x] Bulk import — `POST /api/import` accepts up to 500 ISBNs, fetches/refreshes OpenLibrary metadata, adds books to the user's library in one request; `BulkImportService` + `ImportController` + `ImportPage` in the client
- [x] Stats — `GET /api/stats` (Admin+) returns catalogue counts, user-book aggregates (owned vs wishlist, read vs unread, total read records), top-10 authors/subjects, active member count; `StatsPage` in the client
- [x] Dev auth bypass — `DevAuthController` (`#if DEBUG` + `IsDevelopment()` double-guard) issues real JWTs for named personas without OAuth; `DevLoginPage` in the client
- [x] Mock data seeder — `MockDataSeeder` (`#if DEBUG`) creates 10 realistic member accounts and populates their libraries via OpenLibrary Search API; ISBN results cached to `seed-data/mock-isbn-cache.json` so subsequent reseeds skip the network; popular/niche book distribution creates realistic multi-user overlap
- [x] Discovery APIs — `GET /api/trending` (Open Library daily/weekly, 1 h cache, no key required); `GET /api/bestseller/{list}` (NYT Books API, 24 h cache, requires `NytBooks:ApiKey`); `GET /api/bestseller` returns available list names. `NytBooksService` + `OpenLibraryClient.GetTrendingAsync`. New shared DTOs: `BestsellerListDto`, `BestsellerEntryDto`, `TrendingResultDto`, `TrendingWorkDto`.
