package state

import (
	"encoding/json"
	"log/slog"
	"os"
	"path/filepath"
	"sync"
	"time"
)

type StateFile struct {
	Version      int                   `json:"version"`
	Files        map[string]*FileState `json:"files"`
	RecentHashes []string              `json:"recent_hashes,omitempty"`
}

type FileState struct {
	Offset     int64     `json:"offset"`
	LinesSent  int64     `json:"lines_sent"`
	LineCount  int64     `json:"line_count"`
	LastSynced time.Time `json:"last_synced"`
}

const maxRecentHashes = 1000

type Store struct {
	mu       sync.Mutex
	path     string
	data     StateFile
	hashSet  map[string]struct{}
}

func NewStore(path string) *Store {
	return &Store{
		path:    path,
		data:    StateFile{Version: 1, Files: make(map[string]*FileState)},
		hashSet: make(map[string]struct{}),
	}
}

func (s *Store) Load() error {
	s.mu.Lock()
	defer s.mu.Unlock()

	raw, err := os.ReadFile(s.path)
	if os.IsNotExist(err) {
		return nil // fresh start
	}
	if err != nil {
		return err
	}
	if err := json.Unmarshal(raw, &s.data); err != nil {
		return err
	}

	// Populate hash lookup set from persisted recent hashes.
	s.hashSet = make(map[string]struct{}, len(s.data.RecentHashes))
	for _, h := range s.data.RecentHashes {
		s.hashSet[h] = struct{}{}
	}

	return nil
}

func (s *Store) Save() error {
	s.mu.Lock()
	defer s.mu.Unlock()

	raw, err := json.MarshalIndent(s.data, "", "  ")
	if err != nil {
		return err
	}

	// Ensure parent directory exists.
	if err := os.MkdirAll(filepath.Dir(s.path), 0o755); err != nil {
		return err
	}

	// Atomic write: write to tmp file, then rename.
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, raw, 0o644); err != nil {
		return err
	}
	return os.Rename(tmp, s.path)
}

func (s *Store) Get(filePath string) (int64, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()

	fs, ok := s.data.Files[filePath]
	if !ok {
		return 0, false
	}
	return fs.Offset, true
}

// GetLineCount returns the stored line count for a file.
func (s *Store) GetLineCount(filePath string) int64 {
	s.mu.Lock()
	defer s.mu.Unlock()

	fs, ok := s.data.Files[filePath]
	if !ok {
		return 0
	}
	return fs.LineCount
}

func (s *Store) Update(filePath string, offset int64, lineCount int64) {
	s.mu.Lock()
	defer s.mu.Unlock()

	fs, ok := s.data.Files[filePath]
	if !ok {
		fs = &FileState{}
		s.data.Files[filePath] = fs
	}
	fs.Offset = offset
	fs.LinesSent += lineCount
	fs.LineCount += lineCount
	fs.LastSynced = time.Now()
}

// HasHash returns true if the hash is in the recent hashes ring buffer.
func (s *Store) HasHash(hash string) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	_, ok := s.hashSet[hash]
	return ok
}

// AddHashes appends hashes to the recent ring buffer, evicting oldest if over cap.
func (s *Store) AddHashes(hashes []string) {
	s.mu.Lock()
	defer s.mu.Unlock()

	for _, h := range hashes {
		if _, ok := s.hashSet[h]; ok {
			continue
		}
		s.data.RecentHashes = append(s.data.RecentHashes, h)
		s.hashSet[h] = struct{}{}
	}

	// Evict oldest if over cap.
	if len(s.data.RecentHashes) > maxRecentHashes {
		excess := len(s.data.RecentHashes) - maxRecentHashes
		for _, h := range s.data.RecentHashes[:excess] {
			delete(s.hashSet, h)
		}
		s.data.RecentHashes = s.data.RecentHashes[excess:]
	}
}

// SetTailOffsets sets offsets to current file sizes for all discovered files,
// so only new data written after this point will be synced.
func (s *Store) SetTailOffsets(files []string) {
	s.mu.Lock()
	defer s.mu.Unlock()

	for _, f := range files {
		info, err := os.Stat(f)
		if err != nil {
			slog.Warn("could not stat file for tail offset", "file", f, "error", err)
			continue
		}
		if _, ok := s.data.Files[f]; !ok {
			s.data.Files[f] = &FileState{}
		}
		s.data.Files[f].Offset = info.Size()
	}
}

func (s *Store) IsEmpty() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.data.Files) == 0
}
