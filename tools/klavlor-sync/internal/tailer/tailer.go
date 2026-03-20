package tailer

import (
	"bytes"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"os"

	"github.com/klavlor/klavlor-sync/internal/model"
)

// ReadResult holds records parsed from new lines and the updated byte offset.
type ReadResult struct {
	Records   []model.LootRecord
	NewOffset int64
	LinesRead int64
}

// ReadNewLines opens the file read-only, seeks to the given offset, reads all
// complete lines (up to the last \n), and parses each as a LootRecord.
// relPath is the relative file path used for content hash computation.
// startLine is the line number of the first line at the given offset.
// Returns parsed records, the new byte offset, and the number of lines read.
func ReadNewLines(path string, offset int64, relPath string, startLine int64) (ReadResult, error) {
	f, err := os.Open(path) // O_RDONLY — safe to read while RuneLite writes
	if err != nil {
		return ReadResult{}, fmt.Errorf("open %s: %w", path, err)
	}
	defer f.Close()

	// Check for file truncation.
	info, err := f.Stat()
	if err != nil {
		return ReadResult{}, fmt.Errorf("stat %s: %w", path, err)
	}
	if info.Size() < offset {
		slog.Warn("file appears truncated, resetting to start", "file", path,
			"stored_offset", offset, "current_size", info.Size())
		offset = 0
		startLine = 0
	}

	if info.Size() == offset {
		return ReadResult{NewOffset: offset}, nil // no new data
	}

	if _, err := f.Seek(offset, io.SeekStart); err != nil {
		return ReadResult{}, fmt.Errorf("seek %s: %w", path, err)
	}

	buf, err := io.ReadAll(f)
	if err != nil {
		return ReadResult{}, fmt.Errorf("read %s: %w", path, err)
	}

	// Only process up to the last newline to avoid partial writes.
	lastNL := bytes.LastIndexByte(buf, '\n')
	if lastNL < 0 {
		return ReadResult{NewOffset: offset}, nil // no complete line yet
	}
	complete := buf[:lastNL+1]
	newOffset := offset + int64(len(complete))

	lines := bytes.Split(bytes.TrimRight(complete, "\n"), []byte("\n"))
	var records []model.LootRecord
	var linesRead int64

	for i, line := range lines {
		line = bytes.TrimSpace(line)
		if len(line) == 0 {
			linesRead++
			continue
		}
		var rec model.LootRecord
		if err := json.Unmarshal(line, &rec); err != nil {
			slog.Warn("skipping malformed line", "file", path,
				"line_number", startLine+int64(i), "error", err)
			linesRead++
			continue
		}

		// Compute content hash: SHA-256(relPath:lineNumber:rawJSON)
		lineNum := startLine + int64(i)
		hashInput := fmt.Sprintf("%s:%d:%s", relPath, lineNum, string(line))
		hash := sha256.Sum256([]byte(hashInput))
		rec.ContentHash = fmt.Sprintf("%x", hash)

		records = append(records, rec)
		linesRead++
	}

	return ReadResult{Records: records, NewOffset: newOffset, LinesRead: linesRead}, nil
}
