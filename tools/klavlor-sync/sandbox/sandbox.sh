#!/usr/bin/env bash
# Local sandbox harness for klavlor-sync.
#
# Runs the syncer in full isolation against your LOCAL stack — never your real
# ~/.klavlor-sync state and never the production server:
#   * State/config live in sandbox/.sandbox/home (HOME/USERPROFILE redirected),
#     so the syncer's state.json is disposable and your real config is untouched.
#   * Fake RuneLite .log files are generated under sandbox/.sandbox/loots.
#   * A local-only API key is seeded straight into the docker Postgres, attached
#     to the seeded admin user, and the syncer posts to the plain-HTTP dev port
#     (http://localhost:5081) — no self-signed-cert hassle.
#   * All loot uses "Sandbox <name>" sources + a SANDBOX character for easy cleanup.
#
# Usage:
#   ./sandbox.sh all            # seed + generate + run + verify (the full loop)
#   ./sandbox.sh seed           # (re)seed the local API key
#   ./sandbox.sh gen [N]        # write N fresh kills (default 6)
#   ./sandbox.sh live [N]       # append N kills (simulate new live drops), then run
#   ./sandbox.sh stream [N] [S] # drip N kills in, one every S seconds, syncer left running
#   ./sandbox.sh run [SECONDS]  # run the syncer in isolation for SECONDS (default 12)
#   ./sandbox.sh verify         # show sandbox rows landed in the DB
#   ./sandbox.sh clean          # delete sandbox rows + key + character + runtime dir
set -euo pipefail

# ---- config ----------------------------------------------------------------
# Defaults to the HTTPS dev port; --insecure (added below) skips the self-signed
# cert. Override with KLAVLOR_API_URL=http://localhost:5081 to use plain HTTP.
API_URL="${KLAVLOR_API_URL:-https://localhost:7081}"
DB_CONTAINER="${KLAVLOR_DB_CONTAINER:-klavlor-db-1}"
DB_USER="${KLAVLOR_DB_USER:-postgres}"
DB_NAME="${KLAVLOR_DB_NAME:-klavlor}"
USER_ID="${KLAVLOR_USER_ID:-1}"                 # seeded admin@klavlor.com
CHARACTER="SANDBOX"
# Fixed local-only key (53 chars: "klav_" + 48 alphanumerics). Local sandbox only.
API_KEY="${KLAVLOR_SANDBOX_KEY:-klav_sandboxLocalTestKey000000000000000000000000000}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOL_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"        # tools/klavlor-sync
SANDBOX="$SCRIPT_DIR/.sandbox"
HOME_DIR="$SANDBOX/home"
LOOTS_DIR="$SANDBOX/loots"
BIN_DIR="$SANDBOX/bin"

KEY_PREFIX="${API_KEY:0:8}"

# ---- helpers ---------------------------------------------------------------
psql_exec() { docker exec -i "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" "$@"; }

is_windows() { case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) return 0;; *) return 1;; esac; }

# winpath converts a unix-style path to a native Windows path when on Git Bash,
# so the Go binary (which reads USERPROFILE / native paths) sees it correctly.
winpath() { if is_windows; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

sha256_hex() {
  if command -v sha256sum >/dev/null 2>&1; then printf '%s' "$1" | sha256sum | awk '{print $1}';
  else printf '%s' "$1" | openssl dgst -sha256 | awk '{print $NF}'; fi
}

build() {
  echo "==> building syncer + generator"
  ( cd "$TOOL_DIR" && CGO_ENABLED=0 go build -o "$BIN_DIR/klavlor-sync$(is_windows && echo .exe || true)" . )
}

# ---- subcommands -----------------------------------------------------------
seed() {
  local hash; hash="$(sha256_hex "$API_KEY")"
  echo "==> seeding local API key (prefix $KEY_PREFIX) for user $USER_ID"
  psql_exec -v ON_ERROR_STOP=1 -q <<SQL
DELETE FROM "ApiKeys" WHERE "KeyPrefix" = '$KEY_PREFIX';
INSERT INTO "ApiKeys" ("UserId","KeyHash","KeyPrefix","Name","IsActive","CreatedAt","SavedAt")
VALUES ($USER_ID, '$hash', '$KEY_PREFIX', 'Local Sandbox', true, now(), now());
SQL
  echo "    key: $API_KEY"
}

# The SANDBOX character is created implicitly by the first ingest, and an implicitly
# created character is IsVisible=false with a GUID for a display name. Both matter:
# every live surface filters on visibility (LootIngestHandler.IsCharacterVisible), so
# a hidden character publishes to neither the feed nor the roll ticker - the kills
# store fine and nothing appears on screen, which looks like a broken feature rather
# than a hidden character. Runs after ingest, so it is a no-op until the first sync.
show_character() {
  psql_exec -v ON_ERROR_STOP=1 -q <<SQL
UPDATE "GameCharacters"
   SET "IsVisible" = true, "IsAdminHidden" = false, "DisplayName" = '$CHARACTER'
 WHERE "RuneLiteId" = '$CHARACTER';
SQL
}

gen() {
  local n="${1:-6}"
  echo "==> generating $n kills"
  ( cd "$TOOL_DIR" && go run ./sandbox/gen -dir "$LOOTS_DIR" -character "$CHARACTER" -count "$n" )
}

gen_append() {
  local n="${1:-3}"
  local offset="${2:-0}"
  echo "==> appending $n live kills"
  ( cd "$TOOL_DIR" && go run ./sandbox/gen -dir "$LOOTS_DIR" -character "$CHARACTER" -count "$n" -append -offset "$offset" )
}

run() {
  local secs="${1:-12}"
  build
  mkdir -p "$HOME_DIR"
  local exe="$BIN_DIR/klavlor-sync"; is_windows && exe="$exe.exe"

  echo "==> running syncer in isolation for ${secs}s (state in $HOME_DIR/.klavlor-sync)"
  # Redirect the home dir the syncer resolves: Windows reads USERPROFILE, Unix reads HOME.
  if is_windows; then export USERPROFILE="$(winpath "$HOME_DIR")"; else export HOME="$HOME_DIR"; fi

  # --insecure is honored only for localhost targets (see sender.go), so it is
  # a no-op on http and safe here; it lets us hit the https dev port directly.
  "$exe" \
    --api-url "$API_URL" \
    --api-key "$API_KEY" \
    --loots-dir "$(winpath "$LOOTS_DIR")" \
    --insecure \
    --sync-all &
  local pid=$!
  sleep "$secs"
  show_character
  # SIGTERM triggers a final flush + state save, then exit.
  kill -TERM "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  echo "==> syncer stopped"
}

verify() {
  echo "==> sandbox loot in the database:"
  psql_exec -q <<SQL
SELECT "SourceName", count(*) AS kills, bool_or("IsImported") AS imported,
       sum("TotalValue") AS total_gp, max("OccurredAt") AS latest
FROM "LootRecords" WHERE "SourceName" LIKE 'Sandbox %' GROUP BY "SourceName" ORDER BY "SourceName";
SELECT count(*) AS sandbox_rows_total FROM "LootRecords" WHERE "SourceName" LIKE 'Sandbox %';
SELECT "Id","RuneLiteId","DisplayName" FROM "GameCharacters" WHERE "RuneLiteId" = '$CHARACTER' AND "UserId" = $USER_ID;
SQL
}

# reset_runtime clears the disposable per-run state (redirected home + generated
# logs) so a full loop always starts fresh, independent of whether a prior
# `clean` fully succeeded. Does NOT touch the DB.
reset_runtime() {
  kill_stray
  rm -rf "$HOME_DIR" "$LOOTS_DIR"
}

# kill_stray stops any lingering sandbox syncer so its binary/state aren't locked
# (on Windows a locked .exe makes rm -rf leave the runtime dir behind).
kill_stray() {
  if is_windows; then
    MSYS_NO_PATHCONV=1 taskkill //F //IM klavlor-sync.exe //FI "WINDOWTITLE eq *" >/dev/null 2>&1 || true
  else
    pkill -f "$BIN_DIR/klavlor-sync" 2>/dev/null || true
  fi
}

clean() {
  echo "==> deleting sandbox DB rows, key, character, and runtime dir"
  psql_exec -q <<SQL
DELETE FROM "LootRecords" WHERE "SourceName" LIKE 'Sandbox %';
DELETE FROM "ApiKeys" WHERE "KeyPrefix" = '$KEY_PREFIX';
DELETE FROM "GameCharacters" WHERE "RuneLiteId" = '$CHARACTER' AND "UserId" = $USER_ID;
SQL
  kill_stray
  rm -rf "$SANDBOX"
  if [ -e "$SANDBOX" ]; then
    echo "    ⚠ could not fully remove $SANDBOX (a file may still be locked) — retrying"
    sleep 1; rm -rf "$SANDBOX" || true
  fi
  [ -e "$SANDBOX" ] && echo "    ⚠ $SANDBOX still present; remove it manually" || echo "    done"
}

# A full loop always starts from a clean runtime slate so stale offsets can never
# mask freshly generated kills.
all() { reset_runtime; seed; gen "${1:-6}"; run "${2:-12}"; verify; }

live() { gen_append "${1:-3}"; run "${2:-12}"; verify; }

# Drip kills in one at a time with the syncer left running, so the live surfaces
# actually animate: the roll ticker gets a chip every few seconds and the feed
# lanes light up whenever the rotation lands on a source with a real drop.
#
# NOT --sync-all, and that matters. --sync-all flags everything IsImported, and an
# imported record is excluded from the ticker outright and damped in the feed - so
# a simulation using it would show nothing moving. Without it the syncer tails: it
# takes the current end of each log as its starting offset, so whatever is already
# there counts as history and only what this loop appends afterwards syncs, live.
#
# The rotation offset advances per tick so successive kills hit different sources.
# One of them, Sandbox Goblin, drops 155gp of junk - under the feed's 10K floor, so
# those kills appear in the ticker and NOWHERE else, which is the thing worth
# watching for.
stream() {
  local n="${1:-30}"
  local interval="${2:-4}"

  seed
  build
  mkdir -p "$HOME_DIR" "$LOOTS_DIR/$CHARACTER"

  # Something has to exist for the watcher to find and take an offset in.
  if ! ls "$LOOTS_DIR/$CHARACTER"/*.log >/dev/null 2>&1; then
    gen 3
  fi

  local exe="$BIN_DIR/klavlor-sync"; is_windows && exe="$exe.exe"
  if is_windows; then export USERPROFILE="$(winpath "$HOME_DIR")"; else export HOME="$HOME_DIR"; fi

  echo "==> starting syncer (tailing, so appends land as LIVE kills)"
  "$exe"     --api-url "$API_URL"     --api-key "$API_KEY"     --loots-dir "$(winpath "$LOOTS_DIR")"     --insecure &
  local pid=$!

  # Stop the syncer however this exits - finished, Ctrl-C, or error.
  trap 'kill -TERM '"$pid"' 2>/dev/null || true' EXIT INT TERM

  # The syncer polls every 5s, so an interval under that just means kills arrive
  # in small batches rather than singly. That is what real play looks like anyway.
  echo "==> dripping $n kills, one every ${interval}s (Ctrl-C to stop early)"
  sleep 6   # let the syncer take its offsets before the first append
  show_character
  for ((i = 0; i < n; i++)); do
    gen_append 1 "$i" >/dev/null
    show_character
    printf '    kill %d/%d
' "$((i + 1))" "$n"
    sleep "$interval"
  done

  echo "==> done; flushing the syncer"
  sleep 6
  kill -TERM "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  trap - EXIT INT TERM
  verify
}

# ---- dispatch --------------------------------------------------------------
cmd="${1:-all}"; shift || true
case "$cmd" in
  seed)   seed ;;
  gen)    gen "$@" ;;
  live)   live "$@" ;;
  stream) stream "$@" ;;
  run)    run "$@" ;;
  verify) verify ;;
  clean)  clean ;;
  all)    all "$@" ;;
  *) echo "unknown command: $cmd"; sed -n '2,30p' "$0"; exit 2 ;;
esac
