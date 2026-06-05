//go:build darwin

package install

import (
	"bytes"
	"encoding/xml"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"

	"github.com/klavlor/klavlor-sync/internal/config"
)

// label is the launchd LaunchAgent identifier (reverse-DNS, matching the
// default server domain).
const label = "dev.joshuawoodward.klavlor-sync"

func launchAgentsDir() string {
	home, _ := os.UserHomeDir()
	return filepath.Join(home, "Library", "LaunchAgents")
}

func plistPath() string {
	return filepath.Join(launchAgentsDir(), label+".plist")
}

// guiTarget returns the launchd domain target for the current GUI session,
// e.g. "gui/501".
func guiTarget() string {
	return "gui/" + strconv.Itoa(os.Getuid())
}

const plistTemplate = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>%s</string>
  <key>ProgramArguments</key><array><string>%s</string><string>--background</string></array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardErrorPath</key><string>%s</string>
</dict></plist>
`

func createStartupEntry(exePath string) error {
	dir := launchAgentsDir()
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return fmt.Errorf("creating LaunchAgents directory: %w", err)
	}

	// Use a dedicated launchd error log — the app truncates config.LogPath() itself
	// in --background mode, so a separate file avoids two writers clobbering it.
	launchdLog := filepath.Join(config.ConfigDir(), "launchd.err.log")
	script := fmt.Sprintf(plistTemplate, label, xmlEscape(exePath), xmlEscape(launchdLog))
	if err := os.WriteFile(plistPath(), []byte(script), 0o644); err != nil {
		return fmt.Errorf("writing plist: %w", err)
	}

	// Reload so the (possibly updated) plist + binary take effect immediately.
	// bootout first to clear any already-loaded copy; ignore its error since the
	// agent may not be loaded yet.
	loadAgent()
	return nil
}

func removeStartupEntry() error {
	bootoutAgent()
	err := os.Remove(plistPath())
	if os.IsNotExist(err) {
		return nil
	}
	return err
}

func copyExe(src, dst string) error {
	// Same path → nothing to copy.
	srcAbs, _ := filepath.Abs(src)
	dstAbs, _ := filepath.Abs(dst)
	if srcAbs == dstAbs {
		// Still ensure it's executable.
		return os.Chmod(dst, 0o755)
	}

	// Stop any running agent so the binary isn't being executed while we replace
	// it (ignored if not loaded). Unlike Windows, the file can be overwritten even
	// if running, but stopping avoids a stale process lingering on the old binary.
	bootoutAgent()

	if err := atomicCopy(src, dst); err != nil {
		return err
	}
	return os.Chmod(dst, 0o755)
}

// loadAgent boots the agent into the current GUI session, falling back to the
// pre-bootstrap `launchctl load` API on older macOS versions.
func loadAgent() {
	bootoutAgent() // clear any existing registration first
	if err := exec.Command("launchctl", "bootstrap", guiTarget(), plistPath()).Run(); err != nil {
		// Fall back to the legacy API.
		_ = exec.Command("launchctl", "load", "-w", plistPath()).Run()
	}
}

// bootoutAgent unloads the agent if present, tolerating "not loaded" errors.
func bootoutAgent() {
	if err := exec.Command("launchctl", "bootout", guiTarget()+"/"+label).Run(); err != nil {
		// Legacy fallback.
		_ = exec.Command("launchctl", "unload", "-w", plistPath()).Run()
	}
}

func xmlEscape(s string) string {
	var buf bytes.Buffer
	_ = xml.EscapeText(&buf, []byte(s))
	return buf.String()
}
