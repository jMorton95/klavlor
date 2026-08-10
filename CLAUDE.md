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

# Run the unit tests (no database, no Docker)
dotnet test KlavLor.UnitTests/KlavLor.UnitTests.csproj
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
- **KlavLor.Infrastructure** — EF Core DbContext (`DataContext`), repository implementations, OSRS Wiki API client, image caching. Repositories are auto-registered via reflection — see "Repository Auto-Registration" below.
- **KlavLor.Web** — ASP.NET Core host, Razor components, HTMX endpoints, cookie auth + API key auth.
- **KlavLor.IntegrationTests** — xUnit tests over a real PostgreSQL (Testcontainers, `postgres:16-alpine`) with the full migration set applied. See "Integration Tests" below.
- **KlavLor.UnitTests** — xUnit tests over the pure loot maths plus the architecture rules. No database, no Docker. See "Unit Tests" below.

### Repository Auto-Registration

`AddDomainRepositories()` / `AddApplicationRepositories()` in `InfrastructureDependencyConfiguration` scan the Infrastructure assembly for every concrete class and register it against the **first** interface it implements from the Domain (resp. Application) assembly whose name **ends in `Repository`**. That is the whole predicate — the naming convention is just "`I…Repository`", not specifically `IxxxQueryRepository`/`IxxxLogRepository`.

Two failure modes are silent, because nothing throws when the scan misses:

- An interface the scan doesn't match simply has **no registration**, and the first request for it throws at runtime on whichever page needs it.
- The scan takes `FirstOrDefault`, so a class implementing **two** repository interfaces from the same assembly gets exactly one of them registered — which one depends on reflection ordering.

`KlavLor.UnitTests/RepositoryRegistrationTests.cs` enforces all of this: every implemented repository interface is registered, no class claims two per assembly, no interface has two implementations, and every repository actually resolves from a request scope (it builds the real container — no DB is touched, since Npgsql opens no connection until a query).

### The Loot-Log Query Surface Is Five Repositories, Split By Consumer

`ILootLogRepository` / `LootLogRepository` no longer exist. What was one 2,768-line class with 38 methods is now five, grouped by the consumer feature that reads them (matching the "organised by feature, not technical concern" rule above) — interfaces in `Application/Interfaces/Repositories/`, implementations in `Infrastructure/Persistence/EntityFramework/Repositories/Loot/`:

- `ILootLogSearchRepository` — admin sync log, public character list, per-character log search, sources table. Read by `LootLogHandler` and `IngestLogHandler`.
- `ILootSourceDetailRepository` — source detail + paged kills, hover popover, monthly kill trend, collection-log panel. Read by `LootLogHandler`, `SourcePopoverHandler`, `LootCharacterProfileHandler`, `LuckLeaderboardRefreshService`.
- `ILootSessionRepository` — per-source sessions, one session's kills, cross-source session history.
- `ILootFeedRepository` — the live feed's per-tier backfill, the character day feed, the first-time feed.
- `ILootProfileRepository` — profile header, window stats, activity heatmap, monthly trend, personal records, top items, plus bulk deletion by character/user.

`LootLogSharedQueries` holds the two private helpers used by more than one of them (`GetTopDropsForSource`, `NullableTimestampParam`), imported with `using static` so the call sites read unchanged. `SessionSql.GapIslandsWithCap` remains the shared gap-and-islands CTE.

A handler that serves several surfaces injects several of these — that is intended, and is what the old single interface was hiding.

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
- **A page with several independently-loaded panels must stagger them.** Use `DeferredSection` (`Loot/Log/Profile/DeferredSection.razor`) rather than putting `hx-trigger="load"` on every panel: it takes a `DelayMs`, and callers step it by 500ms down the page so one page view doesn't open six concurrent queries. It also distinguishes *queued* (flat placeholder, no spinner) from *in flight* (spinner, driven by htmx's `htmx-request` class and the `.defer-spinner` rule in `app.css`). `CharacterProfile.razor` is the reference caller.

**These rules are enforced by tests, not just documented.** `KlavLor.UnitTests/RazorComponentDataAccessTests.cs` holds three checks:

- `No_razor_component_injects_a_repository` — reflects over the `KlavLor.Web` assembly for `ComponentBase` subclasses with an injected member (`[Inject]` property/field, or a constructor parameter) whose type is an `I*Repository`. This is the rule whose violation caused the outage.
- `No_razor_file_injects_a_repository_type` — a supplementary text scan of the `.razor` files for `@inject` of a repository type, catching declarations the metadata check could miss.
- `No_component_uses_Task_WhenAll` — flags **any** `Task.WhenAll` inside a component. Deliberately conservative: it does not try to classify the operands, because a false negative here is an outage while a false positive is one line of human review. If a flagged `Task.WhenAll` provably touches neither a handler nor the DB, that still needs a human decision — do not add a silent exemption to the test.

### The Loot Feed Loads Its Swimlanes After First Paint

The feed page route (`GetPage`) and both routable feed pages are deliberately **query-free and synchronous** so the shell paints without a DB round-trip (~15ms / 7KB, versus ~1.2s when it was server-rendered). `LootFeedContent` renders skeleton columns plus `hx-trigger="load"` on `#feed-grid-container`, which then fetches `GetGrid`.

That one grid response carries **both** halves of the feed together — the backfill entries and the `sse-connect` attributes — so history and live streaming arrive in the same swap and htmx opens the EventSources as it processes the new nodes. Keep them together: splitting them would open a window where published drops are missed. Do not reintroduce a handler on the page route.

The saved tier filter is appended by site.js's `htmx:configRequest` hook (it matches any `/api/loot/feed/**/grid` request), so the initial fetch is already filtered and `initFeedFilter` must NOT re-fetch — that would double-request and re-open every stream.

`StreamFeed` flushes an SSE comment (`: connected`) immediately. Without it the response head is withheld until the first drop, leaving the browser's EventSource in CONNECTING — indistinguishable from a failed connection, unable to fire `onopen` or detect a dead socket.

### Luck Maths: One Path Only

`SourceLootService` is the **only** place expected kill counts are computed. Never derive a rate from `numerator`/`denominator`/`rolls` at a call site — that bug shipped once in the (now deleted) progression auto-completer and made the same drop read "dry as the desert" on one page and "lucky" on another, because the hand-rolled maths skipped raid unique-table scaling and admin rate modifiers.

- `ExpectedCompletions(...)` returns expected KC; `EffectiveRate(...)` also returns the display string (`"1/540"`).
- **Admin rate modifiers are a global baseline.** They are applied inside the facade, so every surface — collection log, character/source page, luck leaderboard, live feed cards, global source and drop pages — must read its rate through it. Rate columns render `EffectiveRarity`, not the raw stored `Rarity`, so an override or model-derived rate is visible rather than silently applied.
- **Depth-modelled sources are scored per run, never from an aggregate depth.** Doom of Mokhaiotl rolls loot at every delve level a run clears, so `SourceCollection.Runs` carries the derived depth of *every* actual claim and `ExpectedCompletionsForRuns` sums the per-run probabilities: `expected runs per drop = runs / Σ P(item | depth_r)`. An earlier version used the character's max-ever depth, which scored shallow runs as if they had all been deep delves and reported everyone as dry. An obtained item is windowed to the runs up to its first receipt (`CollectionEntry.FirstRecordId`); missing items use every run so far. Global (all-players) pages have no run to attribute, so they show no rate for depth-modelled sources rather than assuming a depth.

**Board entry is one rule, ranking is one number.** Any item past its expected roll count qualifies for the dry board — obtained or still being chased, no separate bars — provided it clears the rarity floor (`LuckScore.MinExpectedRollsForBoard` = 100 expected rolls, or a single receipt worth 100k+). Rarity is measured as `ExpectedKc`, never `RarityDenominator`: the latter is 0 for a depth-modelled source and ignores multi-roll tables.

`LuckScore.For(multiple, expectedRolls)` is the sole ranking key, and `GetBoard` orders by it. It exists because the two things that make a streak notable pull apart: improbability depends **only** on the multiple (2× dry is ~13% on a 1/1000 and on a 1/5000 alike), while grind size depends on rarity. Ranking on either alone is wrong — the multiple ignores a 10,000-roll grind, and rolls alone puts a mundane 1× on a rare item above a 5× on a common one. The score is `multiple^1.5 × expectedRolls^(0.316 + 0.02·ln expectedRolls)`; the rarity exponent *grows* with rarity so a 1/5000 is weighted harder than a 1/1000, and `RarityGrowth` is the one tuning knob. The `1.5` sits at the boundary where extreme streaks still hold the top of the board — raise it and the rare-grind preference inverts.

There is deliberately no synthetic ranking multiple any more. The old "rare grind" floor overwrote `Multiple` with `denominator/1000`, which forced the results view to recompute the true ratio for display; `Multiple` is now always the honest figure.

**A depth-modelled source is quoted in delves on the board.** Both `ObservedKc` and `ExpectedKc` are multiplied by the run's average depth, so `Multiple` is mathematically unchanged — `count/expectedRuns` equals `(count·d)/(expectedRuns·d)`. What changes is the unit (a delve is what Doom's rates are per) and therefore the rarity weight `LuckScore` applies: an Eye of ayak becomes a ~1,600-delve grind rather than a ~200-run one, which is what gives Doom uniques standing proportionate to the time they take. `ScoredInDelves` on the entry tells the view which word to print, so no consumer branches on source name. Note the *luck ratio* is still runs-vs-runs everywhere else (see the rule above) — this is a units change on one surface, not a second scale.

**Both sides of a luck ratio are RUNS.** `ExpectedCompletionsForRuns` returns expected *runs*, so every observed figure compared against it must be a run count too — never a delve total. This has been got wrong twice, on the live feed card and on the leaderboard's obtained-item branch (which summed a window's depths and so reported every Doom drop as depth-times drier than it was). `KlavLor.UnitTests/FeedCardDepthTests.cs` pins the scale.

**Feed tiers are per drop, everywhere.** Anything that classifies an item into a swimlane must use the value of a single receipt, never a running total — `LootDropSummary.BestDropValue` exists for exactly this, so 500 cheap drops summing to millions can't read as a legendary. Always classify via `ILootFeedService.GetDropTier` rather than re-hardcoding thresholds; the character/source page's drop grid and the live feed cards share it.

### Item Values: DropsJson Is Raw, The Projection Is Effective

Some genuinely valuable drops are untradeable and so have no Grand Exchange price — the Noxious halberd's three components are worth ~10m each and RuneLite reports every one of them at 0 GP. `ItemValueOverride` lets an admin set a flat intrinsic value per item id, applied through `IItemValueOverrideCache` (singleton, immutable-snapshot swap, primed in Program.cs before the feed seeder — same shape as `ISourceRateModifierCache`).

It is a **global, timeless** override, deliberately not a price history: setting one re-values every past and future receipt of that item. It carries no special-casing downstream, so it flows through ordinary tier classification — 10m lands the drop in Epic, 100k in Uncommon. This is **not** the same thing as `LootDrop.IsSpecial`, which stays what it is: the zero-value admin-injected giga drop for genuine one-offs. The two compose (a special-injected item with a value gets that value) but `IsSpecial` still owns the Legendary lane.

The invariant that makes it consistent:

- **`DropsJson` keeps the RAW RuneLite price and is never rewritten.** It is the canonical record, and it is what makes an override reversible — removal re-derives straight back from it.
- **`LootDrops.Price` and `LootRecords.TotalValue` are the DERIVED projection and hold the EFFECTIVE price.** Every SQL read site therefore needs no change at all, which is why this design was chosen over materialising a parallel `EffectivePrice` column across ~40 query sites.
- **Every site that deserialises `DropsJson` and then looks at a price must call `IItemValueOverrideCache.WithEffectivePrices`.** That is `LootIngestHandler` (both the publish check and the live card), `LootFeedRepository` (`CollapseProjections`, `CollapseDay`), `LootProfileRepository` (biggest kill), `LootSessionRepository` (session kills) and `LootSourceDetailRepository` (notable drops, paged kills, source-session kills). Miss one and the same drop reads one way live and another way after a refresh — the bug class already called out for Doom's depth maths. Sites that only read names and quantities (`LootLogSearchRepository`, `SourceLootService.ParseClaim`, `LootDerivationBackfillService`) correctly do not.
- **Every write re-primes the cache, then re-derives.** `ItemValueOverrideAdminHandler` persists, calls `cache.Replace`, then `RebuildForItem`, which re-prices `LootDrops` from `DropsJson` in bounded batches and rolls `LootRecords.TotalValue` back up with a set-based raw UPDATE (no audit or RowVersion churn — `TotalValue` is derived, not a user edit). Set, change and remove all run the same pass, so it is symmetric and idempotent. It then invalidates `LootStatsCache`, `GlobalSourceCache` and `GlobalDropCache` for exactly what the rebuild touched.

`KlavLor.UnitTests/ItemValueOverrideTests.cs` pins the cache and the tier consequence; `KlavLor.IntegrationTests/ItemValueOverrideRebuildTests.cs` pins the rebuild and the restore-on-removal against real SQL.

The panel also carries an on-request "Find items with no value" report (`FindZeroValueItems`), which is how you discover what needs an override. It is a full scan of the drop table grouped by item, filtered to `MAX(Price) = 0`, so it is deliberately never given a load trigger — the admin has to press the button. Collection-log membership is stamped from `ICollectionLogCache` rather than joined, and sorts those items to the top, because a clog item at 0 GP is the signal and everything below it is usually junk.

### An Admin Edit That Changes A Luck Input Must Request A Rebuild

The luck leaderboard is precomputed hourly, but nearly every admin panel edits an *input* to it: a baseline kill count, a delve depth, a rate modifier, a source or item exclusion, the collection-log blacklist, an intrinsic item value, a source rename, an injected special drop. Before `RecomputeTrigger` existed, all of those left the board quoting the old numbers for up to an hour with nothing on screen saying so.

`RecomputeTrigger.LuckInputsChanged()` (Application/Features/Maintenance) is the single place that mapping lives. It flags a manual run on the existing poll-and-claim `IJobScheduleRepository` rather than recomputing inline — a rebuild walks every character and source, which has no business happening in an admin's request. The services poll once a minute, so it lands within ~60s, and the flag is idempotent, so ten edits cost one rebuild.

**A new admin panel that writes anything the luck maths reads must call it.** It is not auto-registered (it isn't a `*Handler`), so it has an explicit `TryAddScoped` line in `ApplicationDependencyConfiguration`.

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
- `Settings/` — Admin settings hub. One page with many independently-loading HTMX panels: leagues toggle, character baselines, collection-log blacklist, drop-rate resync, failed icons, job health/run history, leaderboard source + item exclusions, item values, source renames, source rate modifiers, special loot
- `Source/` — Global source (boss/monster) detail across all characters, with `GlobalSourceCache`
- `Templates/` — Template CRUD (`Commands/`, `Queries/`) and the visual canvas builder (`Builder/` — nodes, edges, groups, annotations, regions, layouts). There is deliberately no export, import or duplicate feature: the endpoints existed but were never registered or linked from the UI, and were removed rather than finished.
- `Users/` — Admin user management, API key generation/revocation, character assignment
- `Viewer/` — Read-only template viewing, completion tracking

`Loot/` subfolders:

- `Ingest/` — RuneLite batch ingest (+ `Ingest/Audit/` sync logs for the Auditor role)
- `Feed/` — Live SSE feed streams and cards (main + Leagues scopes)
- `Log/` — Per-character loot log, source detail, kill sessions, collection progress
- `Leaderboard/` — Luck leaderboard (spoons / dry streaks) and its source + item exclusion admin
- `SourceModels/` (Application only) — Pluggable per-source drop maths behind `ISourceLootStrategy`: `DefaultSourceLootStrategy`, `DoomLootStrategy` (per-run delve depth), `RaidUniqueShareStrategy`, dispatched by `SourceLootService`, plus admin rate modifiers. **Strategies are matched by interface dispatch — a new strategy must be registered with `SourceLootService` or it silently never engages** (see commit `abf0996`). Enforced by `KlavLor.UnitTests/SourceLootStrategyRegistrationTests.cs`, which scans the Application assembly and fails if any `ISourceLootStrategy` implementation is unregistered or unreachable through the facade.
- `Baseline/` (Application only) — Admin-entered pre-tracking kill counts, added to derived KC
- `Special/` (Application only) — Special/one-off loot item configuration
- `ItemValues/` (Application only) — Admin intrinsic GP values for untradeable drops (see "Item Values" above)

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

Coverage is targeted at the loot-derivation maths and query surface, not the web layer: character sessions, drop-rate bucket client and sync, feed ordinals and one-off drops, loot-drop projection, item-value override rebuild and restore, source rename, source tables, and golden-file assertions for drop search.

```bash
dotnet test KlavLor.IntegrationTests/KlavLor.IntegrationTests.csproj
```

The top-level `tests/` directory holds manual testing-plan docs (Markdown), not runnable tests.

## Unit Tests

`KlavLor.UnitTests` (xUnit) has **no database dependency and needs no Docker**. It references Application (the pure loot maths) and Web (so the architecture tests can reflect over its components). Run it on any machine:

```bash
dotnet test KlavLor.UnitTests/KlavLor.UnitTests.csproj
```

What it covers, and why each file exists:

- `DoomLootStrategyTests` — `ProbabilityOverRun` / `ExpectedCompletionsForRuns` / `EstimateDepth`, including the two properties the strategy's own comments only *claimed*: that the per-run sum reduces to the flat per-run expectation at a uniform depth, and that mixed-depth runs do **not** score as if every run reached the deepest one (the bug that reported everyone as dry). Also pins the level-9 rate clamp for deep delves.
- `SourceLootServiceTests` — that raid unique-table scaling **and** admin rate modifiers are both applied *inside* the facade, so a hand-rolled call site is provably wrong (see "Luck Maths: One Path Only").
- `SourceLootStrategyRegistrationTests` — every `ISourceLootStrategy` in the Application assembly is registered and reachable through `SourceLootService` (commit `abf0996`).
- `RaidAndDefaultStrategyTests`, `LootFeedGroupingTests` — raid share scaling vs. tertiary pass-through; `MaxGap` / `SessionBreakGap` / `PlayDayStart` session boundaries.
- `LootFeedTierClassificationTests` — tiers are per drop, never per running total (500 cheap drops summing to millions must not read as legendary).
- `RepositoryRegistrationTests` — the reflection-based repository registration; see "Repository Auto-Registration".
- `RazorComponentDataAccessTests` — the SSR data-access rules; see "Razor Component Data-Access Rules".
- `ItemValueOverrideTests` — the intrinsic item-value cache and the tier consequence; see "Item Values".

There is still no coverage of Web endpoints.

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

Two test projects. See "Integration Tests" and "Unit Tests" above.

- `KlavLor.IntegrationTests` — the raw-ADO query surface, against a real Postgres via Testcontainers. **Needs Docker.**
- `KlavLor.UnitTests` — the pure loot maths plus the architecture rules (repository registration, SSR data-access). **No database, no Docker.**
