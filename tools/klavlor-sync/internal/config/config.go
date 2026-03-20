package config

import (
	"bytes"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/BurntSushi/toml"
)

type Config struct {
	APIURL        string   `toml:"api_url"`
	APIKey        string   `toml:"api_key"`
	LootsDir      string   `toml:"loots_dir"`
	PollInterval  duration `toml:"poll_interval"`
	BatchSize     int      `toml:"batch_size"`
	FlushInterval duration `toml:"flush_interval"`
	SyncAll       bool     `toml:"-"`
	Tail          bool     `toml:"-"`
}

// duration wraps time.Duration for TOML string parsing (e.g. "5s").
type duration struct {
	time.Duration
}

func (d *duration) UnmarshalText(text []byte) error {
	var err error
	d.Duration, err = time.ParseDuration(string(text))
	return err
}

func DefaultConfig() Config {
	home, _ := os.UserHomeDir()
	return Config{
		APIURL:        "https://localhost:7081",
		LootsDir:      filepath.Join(home, ".runelite", "loots"),
		PollInterval:  duration{5 * time.Second},
		BatchSize:     250,
		FlushInterval: duration{10 * time.Second},
	}
}

// ConfigDir returns the path to the klavlor-sync config directory.
func ConfigDir() string {
	home, _ := os.UserHomeDir()
	return filepath.Join(home, ".klavlor-sync")
}

func ConfigPath() string {
	return filepath.Join(ConfigDir(), "config.toml")
}

func StatePath() string {
	return filepath.Join(ConfigDir(), "state.json")
}

// LogPath returns the path for the background-mode log file.
func LogPath() string {
	return filepath.Join(ConfigDir(), "klavlor-sync.log")
}

// ExePath returns the installed executable path.
func ExePath() string {
	return filepath.Join(ConfigDir(), "klavlor-sync.exe")
}

// writeConfig is the TOML-serializable subset of Config.
type writeConfig struct {
	APIURL   string `toml:"api_url"`
	APIKey   string `toml:"api_key"`
	LootsDir string `toml:"loots_dir"`
	SyncAll  bool   `toml:"sync_all"`
	Tail     bool   `toml:"tail"`
}

// WriteConfig writes the given config to the config file in TOML format.
func WriteConfig(cfg Config) error {
	wc := writeConfig{
		APIURL:   cfg.APIURL,
		APIKey:   cfg.APIKey,
		LootsDir: cfg.LootsDir,
		SyncAll:  cfg.SyncAll,
		Tail:     cfg.Tail,
	}

	var buf bytes.Buffer
	if err := toml.NewEncoder(&buf).Encode(wc); err != nil {
		return fmt.Errorf("encoding config: %w", err)
	}

	return os.WriteFile(ConfigPath(), buf.Bytes(), 0o644)
}

// ParseMode checks os.Args for --install or --uninstall without requiring
// a config file or API key. Returns "install", "uninstall", or "" for normal sync.
func ParseMode() (string, error) {
	for _, arg := range os.Args[1:] {
		switch arg {
		case "--install":
			return "install", nil
		case "--uninstall":
			return "uninstall", nil
		}
	}
	return "", nil
}

// HasBackgroundFlag returns true if --background is present in os.Args.
func HasBackgroundFlag() bool {
	for _, arg := range os.Args[1:] {
		if arg == "--background" {
			return true
		}
	}
	return false
}

func Load() (Config, error) {
	cfg := DefaultConfig()

	path := ConfigPath()
	if _, err := os.Stat(path); err == nil {
		if _, err := toml.DecodeFile(path, &cfg); err != nil {
			return cfg, fmt.Errorf("parsing config %s: %w", path, err)
		}
	}

	// CLI flags override config file values.
	fs := flag.NewFlagSet("klavlor-sync", flag.ContinueOnError)
	apiURL := fs.String("api-url", "", "KlavLor API base URL")
	apiKey := fs.String("api-key", "", "KlavLor API key")
	lootsDir := fs.String("loots-dir", "", "Path to RuneLite loots directory")
	syncAll := fs.Bool("sync-all", false, "Sync all historical data from offset 0")
	tail := fs.Bool("tail", false, "Skip to current file ends (no historical sync)")
	_ = fs.Bool("background", false, "Run in background mode (log to file)")

	if err := fs.Parse(os.Args[1:]); err != nil {
		return cfg, err
	}

	if *apiURL != "" {
		cfg.APIURL = *apiURL
	}
	if *apiKey != "" {
		cfg.APIKey = *apiKey
	}
	if *lootsDir != "" {
		cfg.LootsDir = *lootsDir
	}
	cfg.SyncAll = *syncAll
	cfg.Tail = *tail

	// If config has sync_all/tail from file, load them.
	if !cfg.SyncAll && !cfg.Tail {
		loadSyncMode(path, &cfg)
	}

	if cfg.APIURL == "" {
		return cfg, fmt.Errorf("api_url is required (set in config or --api-url)")
	}
	if cfg.APIKey == "" {
		return cfg, fmt.Errorf("api_key is required (set in config or --api-key)")
	}

	return cfg, nil
}

// loadSyncMode reads sync_all/tail from the config file (these are not in the
// Config struct's toml tags since they're normally CLI-only, but we need them
// for background mode).
func loadSyncMode(path string, cfg *Config) {
	var raw struct {
		SyncAll bool `toml:"sync_all"`
		Tail    bool `toml:"tail"`
	}
	if _, err := os.Stat(path); err != nil {
		return
	}
	if _, err := toml.DecodeFile(path, &raw); err != nil {
		return
	}
	cfg.SyncAll = raw.SyncAll
	cfg.Tail = raw.Tail
}
