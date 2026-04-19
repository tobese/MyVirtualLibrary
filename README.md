# MyVirtualLibrary
Personal book tracker backed by OpenLibrary. ISBN in → edition/work/author/cover cached locally; users manage their own "books", flag them as WantToRead / Owned / Read, and (eventually) lay them out on virtual shelves.
## Solution layout
```
MyVirtualLibrary/
├── VirtualLibrary.Shared/   # DTOs, enums (netstandard2.1, used by API + client)
├── VirtualLibrary.Api/      # ASP.NET Core 10 Web API + EF Core + Identity
│   └── Migrations/          # EF Core migrations (InitialCreate)
├── VirtualLibrary.Client/   # Uno Platform app (net10.0-browserwasm, net10.0-android, net10.0-maccatalyst)
├── docs/
│   └── er-diagram.md        # Mermaid ER diagram of the schema
├── docker-compose.yml       # api + postgres:16
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
- `db` — `postgres:16` with named volume `virtuallibrary-pgdata` on port `5432`.
- `api` — the ASP.NET Core service, built from `VirtualLibrary.Api/Dockerfile`, exposed on `http://localhost:5179`.
- On first boot the API runs `Database.Migrate()` and seeds a SuperAdmin (see below).
Stop with `Ctrl+C`; remove with `docker compose down` (keeps the volume) or `docker compose down -v` (wipes data).
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
2. Run the API pointed at localhost (override the compose-targeted connection string):
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
dotnet run --project VirtualLibrary.Client -f net10.0-browserwasm

# Android (requires the Android workload + running emulator)
dotnet build VirtualLibrary.Client -f net10.0-android

# Mac Catalyst (macOS host only; requires the maccatalyst workload + Xcode for signing)
dotnet build VirtualLibrary.Client -f net10.0-maccatalyst
dotnet run   --project VirtualLibrary.Client -f net10.0-maccatalyst
```
The client's `ApiClient` resolves the API base URL per-platform (`Services/ApiClient.cs`):
- WASM → empty base URL (same-origin; reverse-proxy `/api/*` to the backend in prod, or run with CORS on in dev).
- Android emulator → `http://10.0.2.2:5179`.
- Desktop/iOS/Mac Catalyst → `http://localhost:5179`.
Update those constants if you move the API off `5179`.
## Default credentials
In `Development`, the API seeds one account on startup (`Program.cs → SeedSuperAdminAsync`):
- Email: `admin@virtuallibrary.local`
- Password: `Admin123!`
- Role: `SuperAdmin`, Status: `Active`
Use these on the **Sign in with password** form. External Google/Apple sign-in requires configuration — see the **OAuth setup** section below.
## Useful commands
```bash
# Regenerate EF Core migration
dotnet ef migrations add <Name> --project VirtualLibrary.Api --output-dir Migrations

# Apply pending migrations manually (normally auto-applied in dev)
dotnet ef database update --project VirtualLibrary.Api

# Drop & recreate the database (native setup)
psql -U postgres -d postgres -c "DROP DATABASE IF EXISTS virtuallibrary; CREATE DATABASE virtuallibrary;"

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
- `POST /api/users/{id}/approve`    — `{ "approved": true|false }`
- `POST /api/users/{id}/suspend`    — Admin / SuperAdmin
- `POST /api/users/{id}/reactivate` — Admin / SuperAdmin
- `POST /api/users/{id}/role`       — SuperAdmin (`{ "role": 0|1|2 }`)
- `DELETE /api/users/{id}`          — SuperAdmin
- `POST /api/lookup/{isbn}`         — OpenLibrary lookup, caches to DB
- `GET  /api/books[?status=…]`      — current user's books
- `POST /api/books`                 — `{ "isbn": "...", "status": 0|1|2 }`
- `GET  /api/books/{id}`            — single user-book
- `PATCH /api/books/{id}`           — `{ "status"?, "rating"?, "notes"? }`
- `DELETE /api/books/{id}`
- `GET  /api/shelves/default`         — load-or-create default shelf (unplaced owned books merged in)
- `PUT  /api/shelves/{id}/placements` — `{ "userBookIds": ["<uuid>", …] }` replaces all placements in slot order
- `POST /api/auth/exchange`           — `{ "provider", "code", "codeVerifier", "redirectUri" }` PKCE code exchange (preferred over `/login`)
- `POST /api/import`                  — bulk-import up to 500 ISBNs; fetches/refreshes metadata from OpenLibrary and adds books to the calling user's library. Body: `{ "isbns": ["…"], "defaultStatus": 0|1|2, "defaultIsOwned": true|false }`
- `GET  /api/stats`                   — library-wide statistics (Admin / SuperAdmin only): catalogue counts, user-book aggregates, top authors, top subjects, active member count
- `POST /api/auth/dev-login?persona=<name>` — **Debug builds only**; issues a real JWT for a named test persona without credentials. Personas: `superadmin`, `admin`, `member`, `pending`, `suspended`. Returns 404 in non-Development environments even if compiled as Debug.
Enum reference: `BookStatus 0=WantToRead, 1=Owned, 2=Read`; `UserRole 0=User, 1=Admin, 2=SuperAdmin`; `UserStatus 0=PendingApproval, 1=Active, 2=Rejected, 3=Suspended`.
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
- **`Microsoft.EntityFrameworkCore.Query[20504]` "loads related collections for more than one collection navigation"** — harmless; tracked as a future optimisation to enable `QuerySplittingBehavior.SplitQuery`.
- **WASM build warns `IL2026` on JSON methods** — fixed in this repo: `AppJsonContext` (source-generated `JsonSerializerContext`) covers all DTOs and all `ApiClient` call-sites use the trim-safe `JsonTypeInfo<T>` overloads. If you see new IL2026 warnings after adding a DTO, add a matching `[JsonSerializable]` entry to `VirtualLibrary.Client/Services/AppJsonContext.cs`.
- **Docker Desktop not running** — `docker compose up` fails with `Cannot connect to the Docker daemon`. Start Docker Desktop, or use Option B above.
## Implementation status
See `docs/er-diagram.md` for the data model. Plan progress:
- [x] Shared DTOs, enums, netstandard2.1 polyfill
- [x] AppDbContext + Identity + library tables + InitialCreate migration
- [x] ASP.NET Core API: Auth, Users, Books, Lookup controllers; SuperAdmin seeding
- [x] OpenLibrary client with DB + memory cache and rate limiting
- [x] Docker Compose + multi-stage API Dockerfile
- [x] Uno client pages: Login, PendingApproval, Scan, Library, BookDetail, Shelf, UserManagement
- [x] Android ISBN scanner — `IIsbnScanner` abstraction + `AndroidIsbnScanner` (live `Plugin.Scanner.Uno` path) + `AndroidManifest.xml` permissions + `ScannerBootstrap` DI wiring
- [x] Mac Catalyst ISBN scanner — `net10.0-maccatalyst` TFM + hand-rolled AVFoundation `MacCatalystIsbnScanner` (EAN-13/EAN-8 via `AVCaptureMetadataOutput`) + `Info.plist` / `Entitlements.plist` with `NSCameraUsageDescription` and `com.apple.security.device.camera`; supports built-in, external, and Continuity cameras, degrades to manual entry on camera-less Macs
- [x] Virtual shelf: drag/drop reorder (`ListView` `CanReorderItems`) + physical-dimension spine widths + `ShelvesController` (load-or-create default shelf, batch-replace placements)
- [x] Production OAuth wiring — `ExternalTokenValidatorFactory` (Google via `GoogleJsonWebSignature`, Apple via OIDC discovery + JWKS), `OAuthConfig` for client IDs, configurable via user secrets / env vars; implicit flow wired end-to-end (PKCE upgrade tracked in issue #5)
- [x] Trim-safe WASM — `AppJsonContext` source-generated `JsonSerializerContext` + all `ApiClient` call-sites use `JsonTypeInfo<T>` overloads; zero IL2026 warnings on Release WASM build
- [x] Read record tracking — `ReadRecord` entity (start/finish dates) linked to `UserBook`; `IsOwned` flag added to `UserBook`; `AddReadRecordsAndIsOwned` migration
- [x] Bulk import — `POST /api/import` accepts up to 500 ISBNs, fetches/refreshes OpenLibrary metadata, adds books to the user's library in one request; `BulkImportService` + `ImportController` + `ImportPage` in the client
- [x] Stats — `GET /api/stats` (Admin+) returns catalogue counts, user-book aggregates (owned vs wishlist, read vs unread, total read records), top-10 authors/subjects, active member count; `StatsPage` in the client
- [x] Dev auth bypass — `DevAuthController` (`#if DEBUG` + `IsDevelopment()` double-guard) issues real JWTs for named personas (`superadmin`, `admin`, `member`, `pending`, `suspended`) without OAuth; `DevLoginPage` in the client
