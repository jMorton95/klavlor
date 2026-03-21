package watcher

import (
	"context"
	"log/slog"
	"os"
	"path/filepath"
	"strings"

	"github.com/klavlor/klavlor-sync/internal/model"
	"github.com/klavlor/klavlor-sync/internal/state"
	"github.com/klavlor/klavlor-sync/internal/tailer"
)

type Watcher struct {
	lootsDir string
	store    *state.Store
	enqueue  func(ctx context.Context, records []model.LootRecord)
}

func New(lootsDir string, store *state.Store, enqueue func(ctx context.Context, records []model.LootRecord)) *Watcher {
	return &Watcher{
		lootsDir: lootsDir,
		store:    store,
		enqueue:  enqueue,
	}
}

// Poll scans all .log files in the loots directory and reads new lines from
// any files that have grown since the last known offset.
func (w *Watcher) Poll(ctx context.Context) {
	files, err := w.discoverFiles()
	if err != nil {
		slog.Error("discovering loot files", "error", err)
		return
	}

	for _, path := range files {
		if ctx.Err() != nil {
			return
		}

		info, err := os.Stat(path)
		if err != nil {
			slog.Debug("stat failed, will retry next cycle", "file", path, "error", err)
			continue
		}

		offset, _ := w.store.Get(path)

		if info.Size() <= offset {
			continue // no new data
		}

		relPath, _ := filepath.Rel(w.lootsDir, path)
		startLine := w.store.GetLineCount(path)

		// Extract character ID from the first path component (e.g., "MyAccount/Slayer.log" → "MyAccount").
		characterId := ""
		if parts := strings.SplitN(filepath.ToSlash(relPath), "/", 2); len(parts) == 2 {
			characterId = parts[0]
		}

		result, err := tailer.ReadNewLines(path, offset, relPath, startLine)
		if err != nil {
			slog.Debug("read failed, will retry next cycle", "file", path, "error", err)
			continue
		}

		// Filter out records whose hash is already in the recent hashes buffer.
		var filtered []model.LootRecord
		var newHashes []string
		for _, rec := range result.Records {
			if rec.ContentHash != "" && w.store.HasHash(rec.ContentHash) {
				continue
			}
			rec.CharacterId = characterId
			filtered = append(filtered, rec)
			if rec.ContentHash != "" {
				newHashes = append(newHashes, rec.ContentHash)
			}
		}

		if len(filtered) > 0 {
			slog.Info("new entries", "file", relPath, "count", len(filtered))
			w.enqueue(ctx, filtered)
		}

		// Track new hashes in the ring buffer.
		if len(newHashes) > 0 {
			w.store.AddHashes(newHashes)
		}

		w.store.Update(path, result.NewOffset, result.LinesRead)
	}
}

// DiscoverFiles returns all .log file paths under the loots directory.
func (w *Watcher) DiscoverFiles() ([]string, error) {
	return w.discoverFiles()
}

func (w *Watcher) discoverFiles() ([]string, error) {
	var files []string
	err := filepath.WalkDir(w.lootsDir, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return nil // skip inaccessible entries
		}
		if !d.IsDir() && strings.HasSuffix(strings.ToLower(d.Name()), ".log") {
			files = append(files, filepath.Clean(path))
		}
		return nil
	})
	return files, err
}
