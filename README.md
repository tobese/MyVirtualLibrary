# MyVirtualLibrary
Personal book tracker backed by OpenLibrary. ISBN in → edition/work/author/cover cached locally; users manage their own "books", flag them as WantToRead / Owned / Read, and (eventually) lay them out on virtual shelves.
## Solution layout
```
MyVirtualLibrary/
├── VirtualLibrary.Shared/   # DTOs, enums (netstandard2.1, used by API + client)
├── VirtualLibrary.Api/      # ASP.NET Core 10 Web API + EF Core + Identity
│   └── Migrations/          # EF Core migrations (InitialCreate)
├── VirtualLibrary.Client/   # Uno Platform app (net10.0-browserwasm, net10.0-android)
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
The client is a single Uno project targeting WASM and Android.
```bash
# Web (WASM) — opens http://localhost:5000 with hot reload
dotnet run --project VirtualLibrary.Client -f net10.0-browserwasm

# Android (requires the Android workload + running emulator)
dotnet build VirtualLibrary.Client -f net10.0-android
```
The client's `ApiClient` resolves the API base URL per-platform (`Services/ApiClient.cs`):
- WASM → empty base URL (same-origin; reverse-proxy `/api/*` to the backend in prod, or run with CORS on in dev).
- Android emulator → `http://10.0.2.2:5179`.
- Desktop/iOS → `http://localhost:5179`.
Update those constants if you move the API off `5179`.
## Default credentials
In `Development`, the API seeds one account on startup (`Program.cs → SeedSuperAdminAsync`):
- Email: `admin@virtuallibrary.local`
- Password: `Admin123!`
- Role: `SuperAdmin`, Status: `Active`
Use these on the **Sign in with password** form. External Google/Apple sign-in requires real OAuth client IDs — see `appsettings.json` → `Auth:Google` / `Auth:Apple`.
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
Enum reference: `BookStatus 0=WantToRead, 1=Owned, 2=Read`; `UserRole 0=User, 1=Admin, 2=SuperAdmin`; `UserStatus 0=PendingApproval, 1=Active, 2=Rejected, 3=Suspended`.
## Android barcode scanner
The `ScanPage` resolves `VirtualLibrary.Client.Services.IIsbnScanner` via a tiny platform-conditional factory (`Services/IIsbnScanner.cs`). On non-Android heads it falls back to `ManualIsbnScanner` (no-op + camera button disabled); on the Android head it selects `VirtualLibrary.Client.Platforms.Android.AndroidIsbnScanner`.
`AndroidIsbnScanner` currently returns "not supported" so the Scan page stays usable via keyboard entry. A fully-commented `Plugin.Scanner.Uno` integration is present inside an `#if USE_PLUGIN_SCANNER_UNO` block. To enable it:
1. Wait for `Plugin.Scanner.Uno` (currently `0.0.1`) to relax its `Uno.WinUI 6.5.64` dependency — **or** bump `global.json`'s `Uno.Sdk` to a version that pulls `Uno.WinUI >= 6.5.64`.
2. Add a PackageReference for `Plugin.Scanner.Uno` conditioned on android/ios target frameworks, with a matching `PackageVersion` in `Directory.Packages.props`.
3. Define `USE_PLUGIN_SCANNER_UNO` in the Android build (e.g. `<DefineConstants>$(DefineConstants);USE_PLUGIN_SCANNER_UNO</DefineConstants>` inside a `<PropertyGroup Condition="$(TargetFramework.Contains('android'))">`).
4. Wire an `IServiceProvider` (either the Uno.Extensions `.UseScanner()` builder or a plain `ServiceCollection().AddScanner()`) so the `IBarcodeScanner` dependency in `AndroidIsbnScanner` can be resolved.
Camera and flashlight permissions are already declared in `VirtualLibrary.Client/Platforms/Android/AndroidManifest.xml`, so no manifest edits are required.
## Troubleshooting
- **`VirtualLibrary.Shared` fails with `IsExternalInit is not defined`** — the polyfill lives in `VirtualLibrary.Shared/Polyfills.cs`. Don't delete it; it's required because `netstandard2.1` predates C# 9 init-only setters.
- **API boots then dies with `nodename nor servname provided, or not known`** — the default connection string uses `Host=db` (Docker Compose name). For a native run, override `ConnectionStrings__DefaultConnection` as shown above.
- **`Microsoft.EntityFrameworkCore.Query[20504]` "loads related collections for more than one collection navigation"** — harmless; tracked as a future optimisation to enable `QuerySplittingBehavior.SplitQuery`.
- **WASM build warns `IL2026` on JSON methods** — trim-safety warnings. They only matter once IL trimming is turned on; the fix is a `JsonSerializerContext` source-generator covering all `VirtualLibrary.Shared` DTOs.
- **Docker Desktop not running** — `docker compose up` fails with `Cannot connect to the Docker daemon`. Start Docker Desktop, or use Option B above.
## Implementation status
See `docs/er-diagram.md` for the data model. Plan progress:
- [x] Shared DTOs, enums, netstandard2.1 polyfill
- [x] AppDbContext + Identity + library tables + InitialCreate migration
- [x] ASP.NET Core API: Auth, Users, Books, Lookup controllers; SuperAdmin seeding
- [x] OpenLibrary client with DB + memory cache and rate limiting
- [x] Docker Compose + multi-stage API Dockerfile
- [x] Uno client pages: Login, PendingApproval, Scan, Library, BookDetail, Shelf, UserManagement
- [~] Android ISBN scanner — `IIsbnScanner` abstraction + `AndroidIsbnScanner` scaffold + `AndroidManifest.xml` permissions all in place; `Plugin.Scanner.Uno` wiring gated on a `USE_PLUGIN_SCANNER_UNO` flag and blocked on its `Uno.WinUI 6.5.64` pin not matching our `Uno.Sdk 6.5.31`
- [ ] Virtual shelf: drag/drop placements + physical-dimension fallback
- [ ] Production OAuth wiring for Google / Apple
- [ ] Source-gen JSON context for trim-safe WASM
