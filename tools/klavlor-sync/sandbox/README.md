# klavlor-sync local sandbox

A throwaway harness for exercising the syncer end-to-end against your **local**
stack without ever touching your real `~/.klavlor-sync` state or the production
server.

## What it isolates

| Concern | Production / normal use | Sandbox |
|---------|-------------------------|---------|
| Syncer state/config | `~/.klavlor-sync/` | `sandbox/.sandbox/home/.klavlor-sync/` (HOME/USERPROFILE redirected for the child process) |
| Loot logs | `~/.runelite/loots/` | `sandbox/.sandbox/loots/SANDBOX/*.log` (generated) |
| Server | `https://chart.joshuawoodward.dev` | `https://localhost:7081` via `--insecure` (skips the self-signed dev cert; honored for localhost only). Override `KLAVLOR_API_URL=http://localhost:5081` for the plain-HTTP twin. |
| API key | minted in admin UI | seeded straight into local Postgres, attached to `admin@klavlor.com` (user 1) |
| Loot data | your real kills | `Sandbox <name>` sources + `SANDBOX` character — collision-free, easy to delete |

Everything the sandbox creates lives under `.sandbox/` (gitignored) or is tagged
so `clean` removes it completely.

## Prerequisites

1. Local Postgres up: `docker compose up -d` (container `klavlor-db-1`).
2. The web app running on the dev HTTP port. From the repo root:
   ```bash
   dotnet run --project KlavLor.Web/KlavLor.Web.csproj
   ```
   It listens on `https://localhost:7081` **and** `http://localhost:5081`. The
   sandbox targets `:7081` with `--insecure` by default; set
   `KLAVLOR_API_URL=http://localhost:5081` to use the plain-HTTP twin instead.
   Migrations + the admin user seed run automatically.
3. Go toolchain (to build the syncer + the log generator).

## Usage

From this directory (`tools/klavlor-sync/sandbox`):

```bash
./sandbox.sh all          # full loop: seed key -> generate 6 kills -> run syncer -> verify
./sandbox.sh verify       # re-show what landed in the DB
./sandbox.sh live 3       # append 3 NEW kills and sync them as live (non-imported) drops
./sandbox.sh clean        # remove all sandbox rows, the key, the character, and .sandbox/
```

Individual steps: `seed`, `gen [N]`, `run [SECONDS]`.

### Imported vs live

The first `run` uses `--sync-all` on empty state, so records are flagged
`IsImported = true` (historical backfill — only Rare+ drops publish to the SSE
feed, but everything is stored). `live` appends to the existing logs and runs
again with state already present, so those kills ingest as **live** drops and
publish to the loot feed by tier. The bundled generator includes a 2.5M (Rare)
and an 11M (Epic) drop so you can watch the feed light up.

## Verifying by eye

- **DB:** `./sandbox.sh verify` (counts + totals per sandbox source).
- **Feed/UI:** log into `http://localhost:5081` as `admin@klavlor.com` /
  `Admin123456!` and open the loot feed / loot log — sandbox kills show under the
  `SANDBOX` character with `Sandbox …` source names.

## Overrides (env vars)

`KLAVLOR_API_URL`, `KLAVLOR_DB_CONTAINER`, `KLAVLOR_USER_ID`,
`KLAVLOR_SANDBOX_KEY` — see the top of `sandbox.sh`.

## Notes

- The harness runs on Git Bash (Windows) and macOS/Linux. On Windows it converts
  paths with `cygpath` and redirects `USERPROFILE`; on Unix it redirects `HOME`.
- Re-running `all` generates fresh timestamps each time, so content hashes differ
  and new rows insert. Within a single generation the server dedups by
  `(UserId, ContentHash)`, so an accidental double-run is harmless.
