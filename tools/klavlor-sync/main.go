package main

import (
	"bufio"
	"context"
	"fmt"
	"log/slog"
	"os"
	"os/signal"
	"strings"
	"syscall"
	"time"

	"github.com/klavlor/klavlor-sync/internal/config"
	"github.com/klavlor/klavlor-sync/internal/model"
	"github.com/klavlor/klavlor-sync/internal/sender"
	"github.com/klavlor/klavlor-sync/internal/state"
	"github.com/klavlor/klavlor-sync/internal/watcher"
)

func main() {
	slog.SetDefault(slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{
		Level: slog.LevelInfo,
	})))

	cfg, err := config.Load()
	if err != nil {
		slog.Error("configuration error", "error", err)
		os.Exit(1)
	}

	slog.Info("klavlor-sync starting",
		"api_url", cfg.APIURL,
		"loots_dir", cfg.LootsDir,
		"poll_interval", cfg.PollInterval.Duration,
		"batch_size", cfg.BatchSize,
	)

	store := state.NewStore(config.StatePath())
	if err := store.Load(); err != nil {
		slog.Error("loading state", "error", err)
		os.Exit(1)
	}

	snd := sender.New(cfg.APIURL, cfg.APIKey, cfg.BatchSize, cfg.FlushInterval.Duration)

	// First-run: mark records as imported during the initial historical sync.
	isImporting := store.IsEmpty() && !cfg.Tail

	enqueue := func(ctx context.Context, records []model.LootRecord) {
		if isImporting {
			for i := range records {
				records[i].Imported = true
			}
		}
		snd.Enqueue(ctx, records)
	}

	w := watcher.New(cfg.LootsDir, store, enqueue)

	// First-run handling: decide whether to sync historical data.
	if store.IsEmpty() {
		if err := handleFirstRun(cfg, store, w); err != nil {
			slog.Error("first-run setup", "error", err)
			os.Exit(1)
		}
	}

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// Sender flush loop.
	go snd.FlushLoop(ctx)

	// Save state after each successful batch.
	snd.OnSent(func(n int) {
		if err := store.Save(); err != nil {
			slog.Error("saving state", "error", err)
		}
	})

	// Periodic state save — reduces re-read window on crash.
	go func() {
		ticker := time.NewTicker(60 * time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				if err := store.Save(); err != nil {
					slog.Error("periodic state save", "error", err)
				}
			}
		}
	}()

	// Watcher poll loop.
	go func() {
		ticker := time.NewTicker(cfg.PollInterval.Duration)
		defer ticker.Stop()
		// Run first poll immediately (historical data marked as imported).
		w.Poll(ctx)
		isImporting = false // subsequent polls are live data
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				w.Poll(ctx)
			}
		}
	}()

	slog.Info("watching for loot updates (Ctrl+C to stop)")

	// Wait for interrupt or SIGTERM.
	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, os.Interrupt, syscall.SIGTERM)
	<-sigCh

	slog.Info("shutting down...")
	cancel()

	snd.Shutdown()

	if err := store.Save(); err != nil {
		slog.Error("saving state on exit", "error", err)
	}

	slog.Info("state saved. goodbye.")
}

func handleFirstRun(cfg config.Config, store *state.Store, w *watcher.Watcher) error {
	switch {
	case cfg.SyncAll:
		slog.Info("--sync-all: will read all historical data")
		return nil // offsets stay at 0

	case cfg.Tail:
		slog.Info("--tail: skipping to current file ends")
		return tailToEnd(store, w)

	default:
		return promptFirstRun(store, w)
	}
}

func promptFirstRun(store *state.Store, w *watcher.Watcher) error {
	fmt.Println()
	fmt.Println("This looks like a first run — no sync state found.")
	fmt.Println()
	fmt.Println("  [Y] Sync all historical loot data")
	fmt.Println("  [n] Skip — only sync new data going forward")
	fmt.Println()
	fmt.Print("Sync all historical data? [Y/n] ")

	reader := bufio.NewReader(os.Stdin)
	input, _ := reader.ReadString('\n')
	input = strings.TrimSpace(strings.ToLower(input))

	switch input {
	case "", "y", "yes":
		slog.Info("will sync all historical data")
		return nil // offsets stay at 0
	default:
		slog.Info("skipping to current file ends")
		return tailToEnd(store, w)
	}
}

func tailToEnd(store *state.Store, w *watcher.Watcher) error {
	files, err := w.DiscoverFiles()
	if err != nil {
		return fmt.Errorf("discovering files: %w", err)
	}
	store.SetTailOffsets(files)
	slog.Info("set tail offsets", "file_count", len(files))
	return store.Save()
}
