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
./sandbox.sh stream 30 4  # drip 30 kills in, one every 4s, syncer left running
./sandbox.sh clean        # remove all sandbox rows, the key, the character, and .sandbox/
```

Individual steps: `seed`, `gen [N]`, `run [SECONDS]`.

### Watching the live surfaces move (`stream`)

`live` is a one-shot: it appends, syncs, and stops. `stream` is the one to use when
you want to *watch* something — it starts the syncer once and leaves it running,
then drips kills into the logs one at a time, so the roll ticker gets a chip every
few seconds and the swimlanes light up when the rotation lands on a source with a
real drop.

```bash
./sandbox.sh stream            # 30 kills, one every 4s
./sandbox.sh stream 100 2      # 100 kills, one every 2s
```

Open **`https://localhost:7081/loot/feed`** to watch. Use the HTTPS port for the UI:
the plain-HTTP twin returns a 500 on any Razor page, because antiforgery is
configured `SecurePolicy = Always`. `:5081` is still fine for the syncer, which only
posts to the API — `KLAVLOR_API_URL=http://localhost:5081 ./sandbox.sh stream`.

Two details worth knowing:

- **`stream` does not pass `--sync-all`.** That flag marks everything `IsImported`,
  and an imported record is excluded from the roll ticker outright and damped in the
  feed — so a simulation using it would show nothing moving. Without it the syncer
  tails: whatever is already in the logs counts as history, and only what the loop
  appends afterwards syncs, live.
- **`Sandbox Goblin` is the interesting one.** Its whole drop table is 155gp, under
  the feed's 10K floor, so those kills appear on the roll ticker and **nowhere else**
  — which is the thing the ticker exists to show.

The harness also unhides the `SANDBOX` character after each sync. A character created
implicitly by ingest starts `IsVisible = false` with a GUID display name, and every
live surface filters on visibility — so without that step the kills store correctly
and absolutely nothing appears on screen, which looks like a broken feature rather
than a hidden character.

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
