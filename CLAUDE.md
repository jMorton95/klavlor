# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Start PostgreSQL (required before running the app)
docker compose up -d

# Run app with hot-reload (Tailwind watch + dotnet watch + browser open)
npm run dev

# Build the full solution
dotnet build KlavLor.slnx

# Run just the web project
dotnet run --project KlavLor.Web/KlavLor.Web.csproj

# Build Tailwind CSS once (also runs automatically before dotnet build via .csproj target)
npm run build:css
```

**App URL:** https://localhost:7081

**Database:** PostgreSQL on localhost:5432, database `klavlor`, user/password `postgres/postgres` (local dev via compose.yaml). Migrations run automatically on startup.

### EF Core Migrations

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project KlavLor.Infrastructure --startup-project KlavLor.Web

# Remove last migration (if not applied)
dotnet ef migrations remove --project KlavLor.Infrastructure --startup-project KlavLor.Web
```

## Architecture

Clean Architecture with four layers. Dependencies flow inward: Web → Application → Domain, Infrastructure → Domain.

- **KlavLor.Domain** — Entities, repository interfaces, domain services. No external dependencies.
- **KlavLor.Application** — Feature handlers (CQRS-style), FluentValidation validators, the Result pattern. Handlers and validators are auto-registered via assembly scanning (classes ending in `*Handler` get scoped registration).
- **KlavLor.Infrastructure** — EF Core DbContext (`DataContext`), repository implementations, OSRS Wiki API client, image caching. Repositories are auto-registered via reflection (scans for `IxxxRepository` implementations in Domain and `IxxxQueryRepository`/`IxxxLogRepository` in Application).
- **KlavLor.Web** — ASP.NET Core host, Razor components, HTMX endpoints, cookie auth + API key auth.

### Key Patterns

**Endpoint registration:** Each endpoint class implements `IEndpoint` with a static `MapEndpoint` method. All endpoints are registered via `MapApplicationRequestHandlers()` in Program.cs.

**Handler flow:** Endpoint receives request → validates via FluentValidation → handler loads aggregate root from repository → calls domain method on entity → saves via repository → returns `Result<T>`.

**Result pattern:** `Result<T>` with `Success(T)`, `Failure(string)`, and `ValidationFailure(errors)` variants. Handlers never throw; they return Result.

**HTMX integration:** Endpoints return Razor components via `IResultExtensions.Component<TComponent>()`. Other HTMX results: `HtmxRedirect()`, `HtmxRefresh()`, `HtmxRetargetResult<T>()`. Auth redirects are HTMX-aware (returns HX-Redirect header instead of 302 when HX-Request header present).

**Route conventions:** Routes are defined as constants in `AppRoutes.cs`. The `.FromApi()` helper prepends `/api` to all route paths.

### Strict DI Rules

- **Never use `Task.Run`, fire-and-forget (`_ = Task...`), or discard async operations.** All async work must be awaited inline within the request pipeline. The scoped DbContext/Npgsql connection is not thread-safe — concurrent access causes `Connection is busy` errors.
- **Never manually create DI scopes** (`IServiceScopeFactory`, `CreateScope()`, `GetRequiredService()`) in request-scoped code. Always use constructor injection. The only acceptable places for manual scopes are `Program.cs` startup and `BackgroundService` implementations.
- **Never instantiate services directly** (`new HttpClient()`, `new SomeRepository()`). Use the registered DI abstractions.

### Rate Limiting Policies

New endpoints must use one of these existing policies (applied via `.RequireRateLimiting("name")`):

- **login** — Per-IP: 5 attempts/min (anonymous auth endpoints)
- **mutation** — Per-user: 60 requests/min (node/edge/group/template CRUD, completion)
- **position** — Per-user: 300 requests/min (high-frequency drag updates)
- **loot-ingest** — Per-user: 120 requests/min (RuneLite plugin ingestion)
- **anonymous** — Per-IP: 120 requests/min (read-only endpoints)

### Authentication & Authorization

- **Cookie auth** (`KlavLor.Web.Auth`) — 30-day sliding expiration, HttpOnly, Strict SameSite
- **API key auth** — Alternative scheme for programmatic access (e.g., RuneLite plugin). Both schemes are accepted on User/Admin policies.
- **ICurrentUser** — Injected into handlers for auth checks (`UserId`, `IsAdmin`). Authorization is checked inline in handlers, not via middleware attributes (beyond `[Authorize]`).
- **Policies:** `User` (any authenticated user) and `Admin` (requires Admin role). Default policy requires authentication.

### Feature Folder Structure

Code is organized by feature, not by technical concern. Both Application and Web layers mirror the same feature folders:

- `Builder/` — Node, edge, group, annotation, region, and layout CRUD for template canvas
- `Viewer/` — Read-only template viewing, completion tracking
- `Templates/` — Template CRUD, import/export, duplication
- `Login/` — Authentication
- `Users/` — Admin user management, API key generation/revocation
- `Loot/` — RuneLite drop ingest, feed streams (standard/notable/mega), loot log

### Frontend

- **Tailwind CSS 4** — Config lives in `KlavLor.Web/wwwroot/app.css` using `@theme` blocks (no tailwind.config.js). Output: `wwwroot/styles.css`.
- **HTMX** — Server-driven interactivity, bundled as `wwwroot/htmx.min.js`.
- **builder.js** — Canvas drag/drop, node/edge creation, Bezier paths, zoom/pan. This is the most complex client-side file.
- **CSP note:** `img-src` allows `https://oldschool.runescape.wiki` for OSRS item images. Update the CSP in Program.cs if adding new external image sources.

### Domain Model

`Template` is the aggregate root. It owns `TemplateNodes`, `TemplateEdges`, `TemplateNodeGroups`, `CanvasAnnotations`, `CanvasRegions`, and `LayoutSnapshots` (DAG structure with visual canvas elements). `UserNodeCompletion` tracks per-user progress through nodes. `GearItem`, `ItemIcon`, and `CachedImage` support OSRS Wiki integration. `LootRecord` and `ApiKey` support the loot tracking feature.

All entities extend `Entity` base class (Id, RowVersion, SavedAt, SavedById audit trail).

### EF Core Patterns

- **Optimistic concurrency** via `uint RowVersion` on all entities (EF xmin token on Postgres).
- **Audit interceptor** (`UserIdAuditInterceptor`) automatically sets `SavedById` on every Add/Modify — never set it manually.
- **Private collections** on aggregates (e.g., `Template._nodes`) use `PropertyAccessMode.Field` in EF config.
- **Cascade delete** for owned entities; **restrict** for edges (prevents orphaning nodes).
- **Auto-include** on `User.UserRoles` — roles are always loaded with user queries.

### Background Services

- `ImageCacheBackfillService` — Backfills OSRS Wiki image cache
- `ItemIconBackfillService` — Backfills item icon data

These are the only places where manual DI scopes are acceptable (via `IServiceScopeFactory`).

## Deployment

CI/CD via GitHub Actions (`.github/workflows/pipeline.yml`). Push to `main` builds a Docker image, pushes to GHCR, and deploys via Docker Swarm with Traefik reverse proxy. Production config is in `docker-stack.yml`.

System admin credentials are injected via `SystemConfiguration__SystemUsername` and `SystemConfiguration__SystemPassword` environment variables.

## klavlor-sync (Loot Sync Tool)

A Go binary (`tools/klavlor-sync/`) that monitors RuneLite's Loot Tracker plugin and syncs kill records to the KlavLor server.

### How It Works

1. Polls `~/.runelite/loots/*.log` files every 5 seconds for new JSON lines written by RuneLite
2. Parses each line, computes a SHA-256 content hash (`relPath:lineNumber:rawJSON`) for deduplication
3. Batches up to 250 records and sends them to `POST /api/loot/ingest/batch` with `Authorization: Bearer {api_key}`
4. Server deduplicates by content hash, stores records, and publishes non-imported kills to SSE feed streams
5. Persists file offsets and recent hashes to `~/.klavlor-sync/state.json` for resumable syncing

### Fresh Install

```bash
# Build (from repo root)
cd tools/klavlor-sync && go build -o klavlor-sync.exe .

# Run interactive installer
./klavlor-sync.exe --install
```

The installer walks through 4 steps:
1. **Find RuneLite** — Auto-detects `~/.runelite/loots/` or prompts for manual path
2. **API Key** — Paste a `klav_*` key generated from the KlavLor admin panel (`/admin/users/{id}/api-key/generate`)
3. **Server URL** — Defaults to `https://localhost:7081`, override for production
4. **Historical sync** — Choose to sync all existing loot history or start fresh (tail mode)

After install:
- Config saved to `~/.klavlor-sync/config.toml`
- Binary copied to `~/.klavlor-sync/klavlor-sync.exe`
- Windows startup entry created at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\klavlor-sync.vbs` (launches hidden on login via `--background`)

### Key Commands

```bash
./klavlor-sync.exe --install      # Interactive setup
./klavlor-sync.exe --uninstall    # Remove startup entry, optionally delete config/state
./klavlor-sync.exe --background   # Run headless with log output to ~/.klavlor-sync/klavlor-sync.log
```

### Server-Side Data Flow

Sync tool → `ApiKeyAuthenticationHandler` (Bearer token → SHA256 lookup → user claims) → `LootIngestHandler` (validate, parse dates, deduplicate by content hash, insert) → `LootFeedService` (publish live kills to SSE subscribers at `/api/loot/feed/stream/{standard|notable|mega}`).

Feed tiers: standard (all kills), notable (>5M value), mega (>20M value).

### Go Source Structure

```
tools/klavlor-sync/
├── main.go                       # Entry point, loop coordination
├── internal/
│   ├── config/config.go          # TOML config loading + CLI flags
│   ├── install/                  # Installer logic (install_windows.go for VBS startup)
│   ├── detect/runelite.go        # Auto-detect RuneLite loots directory
│   ├── watcher/watcher.go        # File polling & discovery
│   ├── tailer/tailer.go          # Line-by-line JSON parsing, content hashing
│   ├── sender/sender.go          # HTTP batching, rate limiting (1 req/s), retries (3x exponential)
│   ├── state/state.go            # Offset/hash persistence to state.json
│   └── model/loot.go             # LootRecord & LootDrop DTOs
└── RUNBOOK.md                    # User documentation
```

## No Test Suite

There are currently no test projects in this solution.
