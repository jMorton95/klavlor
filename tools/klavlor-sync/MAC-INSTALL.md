# KlavLor Sync — Mac install (5 minutes)

This installs a small background tool that watches RuneLite's loot logs and syncs
your kills to KlavLor. It starts automatically every time you log in.

## Before you start
- **RuneLite** installed, with the **Loot Tracker** plugin enabled.
- The **binary** I sent you (pick the one for your Mac):
  - Apple Silicon (M1/M2/M3/M4) → `klavlor-sync-macos-arm64`
  - Intel → `klavlor-sync-macos-amd64`
  - Not sure? Open **Terminal** and run `uname -m` — `arm64` = Apple Silicon, `x86_64` = Intel.
- The **API key** I gave you (a long string starting with `klav_`).

## Install

Open **Terminal** and run these, one block at a time. (Assumes the file is in
your **Downloads** folder — adjust the `cd` if not.)

```bash
# 1. Go to where the file is, and give it a simple name
cd ~/Downloads
mv klavlor-sync-macos-* klavlor-sync        # renames whichever arch you got

# 2. Clear the "downloaded from the internet" flag and make it runnable
xattr -c ./klavlor-sync
chmod +x ./klavlor-sync

# 3. Run the installer
./klavlor-sync --install
```

The installer asks four things:

1. **RuneLite folder** — it auto-detects `~/.runelite/loots`; just press **Enter**.
2. **API key** — paste the `klav_…` key I gave you, press **Enter**.
3. **Server URL** — press **Enter** to accept the default.
4. **Sync history?** — `Y` to upload all past loot, or `n` to only sync from now on.

That's it. The tool copies itself to `~/.klavlor-sync/`, starts syncing immediately,
and will relaunch automatically every time you log in. You can delete the file you
downloaded.

## Check it's working

```bash
launchctl list | grep klavlor          # should print a line (it's running)
tail -f ~/.klavlor-sync/klavlor-sync.log   # look for "batch sent"; Ctrl+C to stop watching
```

Then kill something in RuneLite with Loot Tracker on — it should show up in KlavLor
within about 15 seconds.

## If macOS blocks it

If you see *"cannot be opened because Apple cannot check it…"*, the quarantine flag
wasn't cleared. Re-run `xattr -c ./klavlor-sync` (or right-click the file in Finder →
**Open** once), then try again.

## Uninstall later

```bash
~/.klavlor-sync/klavlor-sync --uninstall
```

This stops the auto-start and (optionally) removes its config and data.
