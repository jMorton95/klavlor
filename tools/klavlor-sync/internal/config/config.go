package config

import (
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/BurntSushi/toml"
)

type Config struct {
	APIURL        string        `toml:"api_url"`
	APIKey        string        `toml:"api_key"`
	LootsDir      string        `toml:"loots_dir"`
	PollInterval  duration      `toml:"poll_interval"`
	BatchSize     int           `toml:"batch_size"`
	FlushInterval duration      `toml:"flush_interval"`
	SyncAll       bool          `toml:"-"`
	Tail          bool          `toml:"-"`
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

func configDir() string {
	home, _ := os.UserHomeDir()
	return filepath.Join(home, ".klavlor-sync")
}

func ConfigPath() string {
	return filepath.Join(configDir(), "config.toml")
}

func StatePath() string {
	return filepath.Join(configDir(), "state.json")
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

	if cfg.APIURL == "" {
		return cfg, fmt.Errorf("api_url is required (set in config or --api-url)")
	}
	if cfg.APIKey == "" {
		return cfg, fmt.Errorf("api_key is required (set in config or --api-key)")
	}

	return cfg, nil
}
