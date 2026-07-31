# KlavLor

A web app for planning and tracking Old School RuneScape (OSRS) progression. Users build visual, DAG-style **templates** on a drag-and-drop canvas, track per-user completion through them, and view a live **loot feed** of in-game drops streamed from RuneLite.

Drops are ingested from RuneLite's Loot Tracker plugin via the companion **klavlor-sync** tool (`tools/klavlor-sync/`), classified into value tiers, and published to subscribers over Server-Sent Events.

On top of the raw drop history the app derives kill counts and drop rates from the OSRS Wiki, then reports **luck**: per-character source and collection-log progress, and a site-wide **Luck Leaderboard** of the biggest spoons and driest streaks. The loot feed, character loot logs, source/drop detail pages and the leaderboard are public — no account needed. Templates, the canvas builder, completion tracking and the admin area require sign-in (accounts are created by an administrator).

## Tech Stack

- **Backend:** ASP.NET Core (.NET) in a Clean Architecture layout — Domain, Application, Infrastructure, Web.
- **Database:** PostgreSQL via EF Core (migrations run automatically on startup).
- **Frontend:** Razor components, HTMX for server-driven interactivity, Tailwind CSS 4.
- **Sync tool:** Go binary that monitors RuneLite logs and posts drops to the ingest API.
- **Tests:** xUnit integration tests against a real PostgreSQL via Testcontainers.

## Quick Start

```bash
# 1. Start PostgreSQL (required before running the app)
docker compose up -d

# 2. Run the app with hot-reload (Tailwind watch + dotnet watch + opens browser)
npm run dev

# 3. (optional) Run the integration tests — needs a working Docker daemon
dotnet test KlavLor.IntegrationTests/KlavLor.IntegrationTests.csproj
```

App: <https://localhost:7081>

See [CLAUDE.md](CLAUDE.md) for full build/run/migration commands, architecture details, and conventions. The loot-sync tool has its own [RUNBOOK.md](tools/klavlor-sync/RUNBOOK.md).

## Project Layout

| Path | Description |
| --- | --- |
| `KlavLor.Domain` | Entities, repository interfaces, domain services (no external deps). |
| `KlavLor.Application` | Feature handlers (CQRS-style), validators, the Result pattern. |
| `KlavLor.Infrastructure` | EF Core `DataContext`, repositories, OSRS Wiki client, image caching. |
| `KlavLor.Web` | ASP.NET Core host, Razor components, HTMX endpoints, auth. |
| `KlavLor.IntegrationTests` | xUnit tests over a Testcontainers PostgreSQL with real migrations. |
| `tools/klavlor-sync` | Go tool that syncs RuneLite drops to the ingest API. |

## License

See [LICENSE](LICENSE).
