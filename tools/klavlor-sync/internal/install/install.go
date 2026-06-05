package install

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"github.com/klavlor/klavlor-sync/internal/config"
	"github.com/klavlor/klavlor-sync/internal/detect"
)

const defaultAPIURL = "https://chart.joshuawoodward.dev"

// Install runs the interactive installation flow.
func Install() {
	fmt.Println()
	fmt.Println("=== KlavLor Sync Setup ===")
	fmt.Println()

	reader := bufio.NewReader(os.Stdin)

	// Step 1: Find RuneLite loots directory.
	fmt.Println("[1/4] Finding RuneLite...")
	lootsDir, err := detect.FindLootsDir()
	if err != nil {
		fatal("RuneLite detection failed: %v", err)
	}
	fmt.Printf("  ✓ Found: %s\n\n", lootsDir)

	// Step 2: API key.
	fmt.Println("[2/4] API Key")
	fmt.Print("  Paste the API key you received: ")
	apiKey, _ := reader.ReadString('\n')
	apiKey = strings.TrimSpace(apiKey)
	if apiKey == "" {
		fatal("API key cannot be empty")
	}
	fmt.Println("  ✓ Saved")
	fmt.Println()

	// Step 3: API URL.
	fmt.Println("[3/4] API URL")
	fmt.Printf("  Server URL [%s]: ", defaultAPIURL)
	apiURL, _ := reader.ReadString('\n')
	apiURL = strings.TrimSpace(apiURL)
	if apiURL == "" {
		apiURL = defaultAPIURL
	}
	fmt.Printf("  ✓ Using %s\n\n", apiURL)

	// Step 4: Historical sync.
	fmt.Println("[4/4] Historical Sync")
	fmt.Print("  Sync all existing loot history? [Y/n]: ")
	syncChoice, _ := reader.ReadString('\n')
	syncChoice = strings.TrimSpace(strings.ToLower(syncChoice))
	syncAll := syncChoice == "" || syncChoice == "y" || syncChoice == "yes"
	if syncAll {
		fmt.Println("  ✓ Will sync historical data on first run")
	} else {
		fmt.Println("  ✓ Will only sync new data going forward")
	}
	fmt.Println()

	// Write config.
	fmt.Println("Installing...")

	cfg := config.Config{
		APIURL:   apiURL,
		APIKey:   apiKey,
		LootsDir: lootsDir,
		SyncAll:  syncAll,
		Tail:     !syncAll,
	}

	dir := config.ConfigDir()
	if err := os.MkdirAll(dir, 0o755); err != nil {
		fatal("creating config directory: %v", err)
	}

	if err := config.WriteConfig(cfg); err != nil {
		fatal("writing config: %v", err)
	}
	fmt.Printf("  ✓ Config → %s\n", config.ConfigPath())

	// Copy exe.
	exePath, err := os.Executable()
	if err != nil {
		fatal("locating current executable: %v", err)
	}
	dst := config.ExePath()
	if err := copyExe(exePath, dst); err != nil {
		fatal("copying executable: %v", err)
	}
	fmt.Printf("  ✓ Syncer → %s\n", dst)

	// Create startup entry.
	binName := filepath.Base(config.ExePath())
	if err := createStartupEntry(dst); err != nil {
		fmt.Printf("  ⚠ Could not create auto-start entry: %v\n", err)
		fmt.Printf("    You can manually run %s --background at login.\n", binName)
	} else {
		fmt.Println("  ✓ Auto-start enabled (runs silently on login)")
	}

	fmt.Println()
	fmt.Println("Install complete! Sync will also start automatically on login.")
	fmt.Printf("To uninstall later: %s --uninstall\n", binName)
	fmt.Println()
	fmt.Println("Starting sync now...")
	fmt.Println()
}

// Uninstall removes the startup entry and optionally the config/state.
func Uninstall() {
	fmt.Println()
	fmt.Println("=== KlavLor Sync Uninstall ===")
	fmt.Println()

	if err := removeStartupEntry(); err != nil {
		fmt.Printf("  ⚠ Could not remove auto-start entry: %v\n", err)
	} else {
		fmt.Println("  ✓ Auto-start removed")
	}

	reader := bufio.NewReader(os.Stdin)
	fmt.Print("  Remove config and state? [y/N]: ")
	input, _ := reader.ReadString('\n')
	input = strings.TrimSpace(strings.ToLower(input))

	if input == "y" || input == "yes" {
		dir := config.ConfigDir()

		// Delete individual files first — the exe is locked by this process.
		for _, name := range []string{"config.toml", "state.json", "klavlor-sync.log", "launchd.err.log"} {
			p := filepath.Join(dir, name)
			if err := os.Remove(p); err != nil && !os.IsNotExist(err) {
				fmt.Printf("  ⚠ Could not remove %s: %v\n", p, err)
			}
		}
		fmt.Println("  ✓ Config and state removed")

		// Try to remove the exe and directory — will fail if we're running from it.
		exePath := config.ExePath()
		if err := os.Remove(exePath); err != nil && !os.IsNotExist(err) {
			fmt.Printf("  ⚠ Could not remove %s (in use). Delete manually or it will be cleaned up on reinstall.\n", exePath)
		}
		_ = os.Remove(dir) // remove dir if now empty
	} else {
		fmt.Printf("  Config kept at %s\n", config.ConfigDir())
	}

	fmt.Println()
	fmt.Println("Uninstall complete.")
}

func fatal(format string, args ...any) {
	fmt.Printf("\n  ✗ Error: "+format+"\n", args...)
	os.Exit(1)
}
