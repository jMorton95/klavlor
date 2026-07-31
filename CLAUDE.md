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

# Run the integration tests (needs a working Docker daemon — Testcontainers starts its own Postgres)
dotnet test KlavLor.IntegrationTests/KlavLor.IntegrationTests.csproj
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
- **KlavLor.IntegrationTests** — xUnit tests over a real PostgreSQL (Testcontainers, `postgres:16-alpine`) with the full migration set applied. See "Integration Tests" below.

### Key Patterns

**Endpoint registration:** Each endpoint class implements `IEndpoint` with a static `MapEndpoint` method and is declared `sealed` (endpoints are never subclassed — `MapEndpoint` is static, so inheritance buys nothing). Registration is **not** automatic — every new endpoint class must be added to the explicit `MapEndpoints<T>()` list in `KlavLor.Web/Configuration/ConfigureEndpoints.cs` (called from `MapApplicationRequestHandlers()` in Program.cs), or its routes silently 404.

**Handler flow:** Endpoint receives request → validates via FluentValidation → handler loads aggregate root from repository → calls domain method on entity → saves via repository → returns `Result<T>`.

**Result pattern:** `Result<T>` with `Success(T)`, `Failure(string)`, and `ValidationFailure(errors)` variants. Handlers never throw; they return Result.

**HTMX integration:** Endpoints return Razor components via `IResultExtensions.Component<TComponent>()`. Other HTMX results: `HtmxRedirect()`, `HtmxRefresh()`, `HtmxRetargetResult<T>()`. These are C# extension members hanging off `IResultExtensions`, defined in `KlavLor.Web/Application/HttpResults/`. Auth redirects are HTMX-aware (returns HX-Redirect header instead of 302 when HX-Request header present).

**Never name a namespace `Results`.** That folder is `HttpResults`, not `Results`, on purpose: a `KlavLor.Web.Application.Results` namespace shadows the framework's `Microsoft.AspNetCore.Http.Results` static class for every file under `KlavLor.Web.Application.*`, because C# resolves enclosing-namespace members before using-directives. It previously forced ~117 call sites to write `Microsoft.AspNetCore.Http.Results.Ok(...)` in full. Plain `Results.Ok(...)` / `Results.NotFound()` now works everywhere; keep it that way.

**Route conventions:** Routes are defined as constants in `AppRoutes.cs`. The `.FromApi()` helper prepends `/api` to all route paths.

### Strict DI Rules

- **Never use `Task.Run`, fire-and-forget (`_ = Task...`), or discard async operations.** All async work must be awaited inline within the request pipeline. The scoped DbContext/Npgsql connection is not thread-safe — concurrent access causes `Connection is busy` errors.
- **Never manually create DI scopes** (`IServiceScopeFactory`, `CreateScope()`, `GetRequiredService()`) in request-scoped code. Always use constructor injection. The only acceptable places for manual scopes are `Program.cs` startup and `BackgroundService` implementations.
- **Never instantiate services directly** (`new HttpClient()`, `new SomeRepository()`). Use the registered DI abstractions.

### Razor Component Data-Access Rules

A production incident (June 2026, fixed in `effdbd6`) was caused by the layout sidebar querying the DB during SSR. Blazor static SSR runs **sibling components' `OnInitializedAsync` concurrently on the same request scope**, so two components loading data in one render pass race on the shared scoped DbContext. EF's "second operation on this context" guard does **not** catch this here, because many repositories execute raw ADO commands directly on `Database.GetDbConnection()` — the race instead corrupts the Npgsql wire protocol (`Received backend message BindComplete while expecting ReadyForQueryMessage`), poisons the connection, and surfaces as intermittent 500s in unrelated requests.

The rules:

- **At most one component per SSR render pass may touch the database.** In practice that is the routed page component only.
- **Layout components and shared/partial components must never query.** They must be parameter-driven, read claims via `ISessionStateManager`, read singleton caches, or fetch their data through their own HTMX request (`hx-trigger="load"` → endpoint → handler), which runs in its own request scope. Reference example: `SidebarAccountEndpoint` + the placeholder in `SidebarComponent.razor`.
- **Components must never inject repositories** (`I*Repository`). When a page component loads its own data, it goes through a `*Handler`. There are no exceptions.
- **Multiple loads inside one component must be awaited sequentially** — never `Task.WhenAll` over handler or repository calls.

### Luck Maths: One Path Only

`SourceLootService` is the **only** place expected kill counts are computed. Never derive a rate from `numerator`/`denominator`/`rolls` at a call site — that bug shipped once in the (now deleted) progression auto-completer and made the same drop read "dry as the desert" on one page and "lucky" on another, because the hand-rolled maths skipped raid unique-table scaling and admin rate modifiers.

- `ExpectedCompletions(...)` returns expected KC; `EffectiveRate(...)` also returns the display string (`"1/540"`).
- **Admin rate modifiers are a global baseline.** They are applied inside the facade, so every surface — collection log, character/source page, luck leaderboard, live feed cards, global source and drop pages — must read its rate through it. Rate columns render `EffectiveRarity`, not the raw stored `Rarity`, so an override or model-derived rate is visible rather than silently applied.
- **Depth-modelled sources are scored per run, never from an aggregate depth.** Doom of Mokhaiotl rolls loot at every delve level a run clears, so `SourceCollection.Runs` carries the derived depth of *every* actual claim and `ExpectedCompletionsForRuns` sums the per-run probabilities: `expected runs per drop = runs / Σ P(item | depth_r)`. An earlier version used the character's max-ever depth, which scored shallow runs as if they had all been deep delves and reported everyone as dry. An obtained item is windowed to the runs up to its first receipt (`CollectionEntry.FirstRecordId`); missing items use every run so far. Global (all-players) pages have no run to attribute, so they show no rate for depth-modelled sources rather than assuming a depth.

**Dry-board entry rules.** Not-yet-obtained items join the dry streak board as soon as the character has done enough kills to have expected the drop once (`MinMissingMultiple` = 1.0) — a 1/100 item still missing at 101 kills shows as 1× dry. Items already obtained keep the 2× bar (`MinMultiple`), since a drop that arrived slightly late isn't worth a slot. The bottom-end rarity filter still drops anything more common than 1/100, or a 1× bar would flood the board with commons. `GetBoard` orders by tier descending, so if the 200-row cap bites it trims the mildest streaks first.

**Feed tiers are per drop, everywhere.** Anything that classifies an item into a swimlane must use the value of a single receipt, never a running total — `LootDropSummary.BestDropValue` exists for exactly this, so 500 cheap drops summing to millions can't read as a legendary. Always classify via `ILootFeedService.GetDropTier` rather than re-hardcoding thresholds; the character/source page's drop grid and the live feed cards share it.

### Progression Completion Is Manual Only

Template-node completion happens **only** when a user clicks a node in the viewer. There is deliberately no drop-driven auto-completion and no generated completion notes — that feature was removed, along with `Features/Progression/` and the `GetAutoCompletableNodes`/`AddCompletions` repository methods. Loot ingest must never write to `UserNodeCompletions`.

### Rate Limiting Policies

New endpoints must use one of these existing policies (applied via `.RequireRateLimiting("name")`):

- **login** — Per-IP: 5 attempts/min (anonymous auth endpoints)
- **mutation** — Per-user: 60 requests/min (node/edge/group/template CRUD, completion)
- **position** — Per-user: 300 requests/min (high-frequency drag updates)
- **loot-ingest** — Per-user: 120 requests/min (RuneLite plugin ingestion)
- **anonymous** — Per-IP: 120 requests/min (read-only endpoints)
- **sse** — Per-IP: 20 *concurrent* connections, no queue (SSE feed streams only)

`sse` is a concurrency limiter, not a fixed window: feed streams stay open for the life of the page, so the limit that matters is simultaneous sockets per IP, not requests per minute. One feed page opens five streams (one per tier), so 20 permits ≈ 4 tabs. `QueueLimit` is 0 so excess connections get an immediate 429 instead of a hung `EventSource`. Any new long-lived/streaming endpoint must use this policy.

### Authentication & Authorization

- **Cookie auth** (`KlavLor.Web.Auth`) — 30-day sliding expiration, HttpOnly, Strict SameSite
- **API key auth** — Alternative scheme for programmatic access (e.g., RuneLite plugin). Both schemes are accepted on User/Admin policies.
- **ICurrentUser** — Injected into handlers for auth checks (`UserId`, `IsAdmin`). Authorization is checked inline in handlers, not via middleware attributes (beyond `[Authorize]`).
- **Policies:** `User` (any authenticated user), `Admin` (requires Admin role), `Auditor` (sync-log access). Default policy requires authentication.

**Public read surface.** Anonymous by design: loot feed (+ Leagues feed and their SSE streams), character loot logs / profiles / source detail / collection tabs, item and source icons, template search, and the Luck Leaderboard. These are linked in the sidebar for signed-out visitors, so an endpoint behind them must be `.AllowAnonymous()` or the nav link 401-redirects to login.

Authenticated (`RoleName.User`) despite also being read-only: the **global** source and drop detail pages (`Features/Source`, `Features/Drop`) and global search. Their routable pages carry `@attribute [Authorize]` to match. Everything that writes or reads one user's own data (templates CRUD, builder, completion, characters, admin, sync logs, ingest) requires authorization too.

**Routable Razor pages authorize separately from endpoints.** `MapRazorComponents<App>()` has no fallback policy, so a `@page` component is anonymous unless it carries `@attribute [Authorize]`. A page and the `/api` endpoint that serves its HTMX-navigated content must agree — otherwise direct URL loads and in-app navigation disagree about who may see it.

### Feature Folder Structure

Code is organized by feature, not by technical concern. Feature folders live under `KlavLor.Application/Features/` and `KlavLor.Web/Application/Features/`. The two layers overlap but don't mirror exactly — Application has top-level `ApiKeys/`, `Builder/`, `CollectionLog/`, `DropRates/` and `Maintenance/` folders whose Web-side counterparts are nested under `Users/`, `Templates/` and `Settings/`.

Shared in both layers:

- `Characters/` — Character profile pages
- `Drop/` — Global drop detail (one item across every source; `GlobalDropCache` memoises the aggregate)
- `Login/` — Authentication (Web also has `Logout/` and `Home/`, which just redirects to the loot feed)
- `Loot/` — the largest area, see breakdown below
- `Search/` — Global search across characters, sources and drops
- `Settings/` — Admin settings hub. One page with many independently-loading HTMX panels: leagues toggle, character baselines, collection-log blacklist, drop-rate resync, failed icons, job health/run history, leaderboard source + item exclusions, source renames, source rate modifiers, special loot
- `Source/` — Global source (boss/monster) detail across all characters, with `GlobalSourceCache`
- `Templates/` — Template CRUD (`Commands/`, `Queries/`) and the visual canvas builder (`Builder/` — nodes, edges, groups, annotations, regions, layouts). There is deliberately no export, import or duplicate feature: the endpoints existed but were never registered or linked from the UI, and were removed rather than finished.
- `Users/` — Admin user management, API key generation/revocation, character assignment
- `Viewer/` — Read-only template viewing, completion tracking

`Loot/` subfolders:

- `Ingest/` — RuneLite batch ingest (+ `Ingest/Audit/` sync logs for the Auditor role)
- `Feed/` — Live SSE feed streams and cards (main + Leagues scopes)
- `Log/` — Per-character loot log, source detail, kill sessions, collection progress
- `Leaderboard/` — Luck leaderboard (spoons / dry streaks) and its source + item exclusion admin
- `SourceModels/` (Application only) — Pluggable per-source drop maths behind `ISourceLootStrategy`: `DefaultSourceLootStrategy`, `DoomLootStrategy` (per-run delve depth), `RaidUniqueShareStrategy`, dispatched by `SourceLootService`, plus admin rate modifiers. **Strategies are matched by interface dispatch — a new strategy must be registered with `SourceLootService` or it silently never engages** (see commit `abf0996`).
- `Baseline/` (Application only) — Admin-entered pre-tracking kill counts, added to derived KC
- `Special/` (Application only) — Special/one-off loot item configuration

Application-only:

- `CollectionLog/` — Collection-log item blacklist admin
- `DropRates/` — Drop-rate admin + resync, missing-rate reporting
- `Maintenance/` — Icon audit, job health, sync status, source rename/admin

Web-only:

- `HealthCheck/` — Health check endpoint

### Frontend

- **Tailwind CSS 4** — Config lives in `KlavLor.Web/wwwroot/app.css` using `@theme` blocks (no tailwind.config.js). Output: `wwwroot/styles.css`.
- **HTMX** — Server-driven interactivity, bundled as `wwwroot/htmx.min.js`.
- **builder.js** — Canvas drag/drop, node/edge creation, Bezier paths, zoom/pan. This is the most complex client-side file.
- **sse.js** — Server-Sent Events client for live loot feed streams (per-tier subscriptions). **site.js** holds shared misc client helpers.
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

All implementations live in `KlavLor.Infrastructure/Services/`, and **all of them are registered in one place** — `AddBackgroundServices()` inside `InfrastructureDependencyConfiguration`, called at the end of `AddInfrastructure()`. Registration used to be split between there and Program.cs, which hid the startup order; don't reintroduce `AddHostedService` calls in Program.cs. Hosted services start in registration order, and `LootFeedSeederService` must stay first.

- `LootFeedSeederService` — Seeds the feed with historical data on startup (must run first)
- `ImageCacheBackfillService` — Backfills OSRS Wiki image cache
- `ItemIconBackfillService` — Backfills item icon data
- `SourceIconBackfillService` — Backfills source (monster/boss) icon data
- `CachedImageReprocessService` — Reprocesses previously cached images
- `CollectionLogSyncService` — Syncs collection-log item definitions from the Wiki
- `DropRateSyncService` — Syncs per-source drop rates from the Wiki
- `LuckLeaderboardRefreshService` — Rebuilds the luck leaderboard hourly
- `LootDerivationBackfillService` — Backfills derived loot columns (effective kills, drop projection)

Recurring services poll `IJobScheduler` for an elapsed interval or a manual admin trigger, and record each cycle via `IJobRunRecorder` for the admin job-health panel (`BackgroundJobNames` holds the job keys).

These are the only places where manual DI scopes are acceptable (via `IServiceScopeFactory`). Program.cs startup also primes the singleton caches (`ICollectionLogCache`, `ISystemSettingsCache`, `ISourceRateModifierCache`) *before* the host starts, because the seeder classifies drops against them immediately.

## Integration Tests

`KlavLor.IntegrationTests` (xUnit) runs against a real PostgreSQL started by Testcontainers (`postgres:16-alpine`) with the full EF migration set applied, so tests exercise real SQL — including the raw-ADO repositories and the `LootDrop` projection — rather than an in-memory fake. **Docker must be running.** The container is shared via `PostgresFixture` / the `"postgres"` collection, so tests in that collection must not assume an empty database; seed and scope your own rows.

Coverage is targeted at the loot-derivation maths and query surface, not the web layer: character sessions, drop-rate bucket client and sync, feed ordinals and one-off drops, loot-drop projection, source rename, source tables, and golden-file assertions for drop search.

```bash
dotnet test KlavLor.IntegrationTests/KlavLor.IntegrationTests.csproj
```

There is no unit-test project and no coverage of Web endpoints/components. The top-level `tests/` directory holds manual testing-plan docs (Markdown), not runnable tests.

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
# Build (from repo root) — Windows
cd tools/klavlor-sync && go build -o klavlor-sync.exe .
./klavlor-sync.exe --install

# Build — macOS (extension-less binary)
cd tools/klavlor-sync && go build -o klavlor-sync .
./klavlor-sync --install
```

`build.sh` cross-compiles release artifacts (Windows `.exe` + macOS universal binary via `lipo` when run on a Mac).

The installer walks through 4 steps:
1. **Find RuneLite** — Auto-detects `~/.runelite/loots/` or prompts for manual path
2. **API Key** — Paste a `klav_*` key generated from the KlavLor admin panel (`/admin/users/{id}/api-key/generate`)
3. **Server URL** — Defaults to `https://localhost:7081`, override for production
4. **Historical sync** — Choose to sync all existing loot history or start fresh (tail mode)

After install:
- Config saved to `~/.klavlor-sync/config.toml`
- Binary copied to `~/.klavlor-sync/klavlor-sync.exe` (Windows) or `~/.klavlor-sync/klavlor-sync` (macOS)
- Auto-start registered (launches hidden on login via `--background`):
  - **Windows** — Startup-folder VBS at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\klavlor-sync.vbs`
  - **macOS** — launchd LaunchAgent at `~/Library/LaunchAgents/dev.joshuawoodward.klavlor-sync.plist` (loaded via `launchctl bootstrap`)

Platform-specific install backends live in `internal/install/install_windows.go` and `install_darwin.go` (Linux/other falls through to the unsupported stub in `install_other.go`). The shared interactive flow and the `atomicCopy` helper are in `install.go` / `install_common.go`.

### Key Commands

```bash
./klavlor-sync.exe --install      # Interactive setup
./klavlor-sync.exe --uninstall    # Remove startup entry, optionally delete config/state
./klavlor-sync.exe --background   # Run headless with log output to ~/.klavlor-sync/klavlor-sync.log
```

### Server-Side Data Flow

Sync tool → `ApiKeyAuthenticationHandler` (Bearer token → SHA256 lookup → user claims) → `LootIngestHandler` (validate, parse dates, deduplicate by content hash, insert) → `LootFeedService` (publish live kills to SSE subscribers at `/api/loot/feed/stream/{standard|uncommon|rare|epic|legendary}`).

Feed tiers are classified **per drop** (not per kill) by GP value, defined as `LootFeedTier` in `ILootFeedService`:
- **Standard** — 10K–100K
- **Uncommon** — 100K–1M
- **Rare** — 1M–10M
- **Epic** — 10M–100M
- **Legendary** — 100M+

Drops below 10K are not published. A single kill can produce entries on multiple tiers.

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

## Tests

See "Integration Tests" above. `KlavLor.IntegrationTests` is the only test project.
