# klavlor-sync Runbook

## Prerequisites

- RuneLite installed with the **Loot Tracker** plugin enabled (writes `.log` files to `~/.runelite/loots/`)
- Go 1.23+ (only needed if building from source)
- A running KlavLor deployment with an API key generated from the admin panel

## Generate an API Key

1. Log in to your KlavLor instance as an admin
2. Go to your user profile and find the **API Key** section
3. Click **Generate** and copy the key -- you won't see it again

## Option A: Interactive Install (Recommended)

Build the binary and run the installer.

**Windows:**

```bash
cd tools/klavlor-sync
go build -o klavlor-sync.exe .
./klavlor-sync.exe --install
```

**macOS** (the binary is extension-less — drop the `.exe`):

```bash
cd tools/klavlor-sync
go build -o klavlor-sync .
./klavlor-sync --install
```

> Building natively on your Mac targets your own CPU automatically. To produce a
> single universal (Intel + Apple-Silicon) binary for distribution, run `./build.sh`
> on a Mac — it merges both architectures with `lipo`.

The installer will:

1. Auto-detect your `~/.runelite/loots/` directory (or prompt you for the path)
2. Ask for your API key
3. Ask for your server URL (defaults to `https://chart.joshuawoodward.dev`)
4. Ask whether to sync all historical loot or start fresh
5. Copy the binary to `~/.klavlor-sync/` (`klavlor-sync.exe` on Windows, `klavlor-sync` on macOS)
6. Register auto-start so it runs silently on login — a Startup-folder VBS entry on
   Windows, a launchd LaunchAgent on macOS

After install, you can delete the original downloaded binary.

## Option B: Manual Setup

Create `~/.klavlor-sync/config.toml`:

```toml
api_url = "https://chart.joshuawoodward.dev"
api_key = "your-api-key-here"
loots_dir = "C:\\Users\\YourName\\.runelite\\loots"  # macOS: "/Users/you/.runelite/loots"
sync_all = true
```

| Key            | Default                    | Description                                     |
|----------------|----------------------------|-------------------------------------------------|
| `api_url`      | `https://localhost:7081`   | Your KlavLor server URL                         |
| `api_key`      | *(required)*               | API key from the admin panel                    |
| `loots_dir`    | `~/.runelite/loots`        | Path to RuneLite's loot log directory           |
| `poll_interval`| `5s`                       | How often to check for new loot entries         |
| `batch_size`   | `250`                      | Records per API request                         |
| `flush_interval`| `10s`                     | Max time before flushing a partial batch        |
| `sync_all`     | `false`                    | Sync all historical data on first run           |
| `tail`         | `false`                    | Skip to end of files, only sync new data        |

Then run:

```bash
cd tools/klavlor-sync
go build -o klavlor-sync.exe .
./klavlor-sync.exe
```

## Running Modes

### Foreground (interactive)

```bash
klavlor-sync.exe
```

Logs to stderr. On first run with no state file, prompts whether to sync history or tail.

### Background (silent)

```bash
klavlor-sync.exe --background
```

Logs to `~/.klavlor-sync/klavlor-sync.log`. On first run, defaults to syncing all historical data without prompting.

### CLI Flag Overrides

Flags override config file values:

```bash
klavlor-sync.exe --api-url https://my-server.dev --api-key abc123 --sync-all
klavlor-sync.exe --tail    # skip history, only watch for new data
```

`--insecure` skips TLS certificate verification, but **only** when the target is
localhost/loopback (e.g. a local dev server on `https://localhost:7081` using the
self-signed ASP.NET dev cert). Against any real (non-loopback) server it is
ignored and verification stays on, so it can't be used to weaken a production
connection. Intended for local development and the sandbox harness.

## File Locations

| File | Path |
|------|------|
| Config | `~/.klavlor-sync/config.toml` |
| State | `~/.klavlor-sync/state.json` |
| Log | `~/.klavlor-sync/klavlor-sync.log` |
| Binary (after install) | `~/.klavlor-sync/klavlor-sync.exe` (Windows) / `~/.klavlor-sync/klavlor-sync` (macOS) |
| Startup entry (Windows) | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\klavlor-sync.vbs` |
| Startup entry (macOS) | `~/Library/LaunchAgents/dev.joshuawoodward.klavlor-sync.plist` |

## First Run Behavior

- **No state file exists + `sync_all`**: reads all `.log` files from offset 0, marks records as historical imports
- **No state file exists + `tail`**: seeks to end of all files, only syncs new entries going forward
- **State file exists**: resumes from last known offsets (normal operation)

## Verifying It Works

1. Check logs for `"batch sent"` messages:
   ```bash
   cat ~/.klavlor-sync/klavlor-sync.log
   ```
2. Kill a monster in RuneLite with Loot Tracker enabled
3. Within ~15 seconds you should see the kill appear in your KlavLor loot feed

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `api_key is required` | No key in config or CLI | Generate a key in the admin panel and add it to `config.toml` |
| `rate limited (429)` | Sending too fast | Normal during large historical syncs -- the syncer backs off automatically using `Retry-After` |
| `batch rejected by server (400)` | Malformed records | Check logs for the response body; these batches are dropped (not retried) |
| `server error 5xx` | Server is down or unhealthy | Retries 3 times with exponential backoff (1s, 4s, 16s), then re-queues |
| Records appearing twice | State file was deleted mid-sync | The syncer deduplicates via content hashing, so duplicates are rare; if they persist, delete `state.json` and re-run with `--sync-all` |
| Loots directory not found | RuneLite not installed or custom path | Pass `--loots-dir /path/to/loots` or set `loots_dir` in config |

## Uninstall

```bash
klavlor-sync.exe --uninstall
```

Removes the Windows Startup entry and optionally deletes `~/.klavlor-sync/` (config, state, binary, logs).
