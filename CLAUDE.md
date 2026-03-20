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
- **KlavLor.Application** — Feature handlers (CQRS-style), FluentValidation validators, the Result pattern. Handlers and validators are auto-registered via assembly scanning.
- **KlavLor.Infrastructure** — EF Core DbContext (`DataContext`), repository implementations, OSRS Wiki API client, image caching. Repositories are auto-registered via reflection (scans for `IxxxRepository` implementations).
- **KlavLor.Web** — ASP.NET Core host, Razor components, HTMX endpoints, cookie auth.

### Key Patterns

**Endpoint registration:** Each endpoint class implements `IEndpoint` with a static `MapEndpoint` method. All endpoints are registered via `MapApplicationRequestHandlers()` in Program.cs.

**Handler flow:** Endpoint receives request → validates via FluentValidation → handler loads aggregate root from repository → calls domain method on entity → saves via repository → returns `Result<T>`.

**Result pattern:** `Result<T>` with `Success(T)`, `Failure(string)`, and `ValidationFailure(errors)` variants. Handlers never throw; they return Result.

**HTMX integration:** Endpoints return Razor components via `IResultExtensions.Component<TComponent>()`. Auth redirects are HTMX-aware (returns HX-Redirect header instead of 302 when HX-Request header present).

### Strict DI Rules

- **Never use `Task.Run`, fire-and-forget (`_ = Task...`), or discard async operations.** All async work must be awaited inline within the request pipeline. The scoped DbContext/Npgsql connection is not thread-safe — concurrent access causes `Connection is busy` errors.
- **Never manually create DI scopes** (`IServiceScopeFactory`, `CreateScope()`, `GetRequiredService()`) in request-scoped code. Always use constructor injection. The only acceptable places for manual scopes are `Program.cs` startup and `BackgroundService` implementations.
- **Never instantiate services directly** (`new HttpClient()`, `new SomeRepository()`). Use the registered DI abstractions.

### Feature Folder Structure

Code is organized by feature, not by technical concern. Both Application and Web layers mirror the same feature folders:

- `Builder/` — Node, edge, and group CRUD for template canvas
- `Viewer/` — Read-only template viewing, completion tracking
- `Templates/` — Template CRUD, import/export, duplication
- `Login/` — Authentication
- `Users/` — Admin user management

### Frontend

- **Tailwind CSS 4** — Config lives in `KlavLor.Web/wwwroot/app.css` using `@theme` blocks (no tailwind.config.js). Output: `wwwroot/styles.css`.
- **HTMX** — Server-driven interactivity, bundled as `wwwroot/htmx.min.js`.
- **builder.js** — Canvas drag/drop, node/edge creation, Bezier paths, zoom/pan. This is the most complex client-side file.

### Domain Model

`Template` is the aggregate root. It owns `TemplateNodes`, `TemplateEdges`, and `TemplateNodeGroups` (DAG structure). `UserNodeCompletion` tracks per-user progress through nodes. `GearItem` and `CachedImage` support OSRS Wiki integration.

All entities extend `Entity` base class (Id, RowVersion, SavedAt, SavedById audit trail).

## Deployment

CI/CD via GitHub Actions (`.github/workflows/pipeline.yml`). Push to `main` builds a Docker image, pushes to GHCR, and deploys via Docker Swarm with Traefik reverse proxy. Production config is in `docker-stack.yml`.

System admin credentials are injected via `SystemConfiguration__SystemUsername` and `SystemConfiguration__SystemPassword` environment variables.

## No Test Suite

There are currently no test projects in this solution.
