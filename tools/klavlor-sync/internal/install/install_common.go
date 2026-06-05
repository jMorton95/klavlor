package install

import (
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
)

// atomicCopy copies src to dst via a temp file + rename so the destination is
// never left half-written. It short-circuits when src and dst resolve to the
// same path. Shared by the per-platform copyExe implementations.
func atomicCopy(src, dst string) error {
	// Normalize paths so comparison works even with mixed separators.
	srcAbs, _ := filepath.Abs(src)
	dstAbs, _ := filepath.Abs(dst)
	if strings.EqualFold(srcAbs, dstAbs) {
		return nil // already in place
	}

	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}

	// Copy via temp file + rename for atomicity.
	tmp := dst + ".tmp"
	srcFile, err := os.Open(src)
	if err != nil {
		return fmt.Errorf("opening source: %w", err)
	}
	defer srcFile.Close()

	dstFile, err := os.Create(tmp)
	if err != nil {
		return fmt.Errorf("creating temp file: %w", err)
	}
	defer dstFile.Close()

	if _, err := io.Copy(dstFile, srcFile); err != nil {
		os.Remove(tmp)
		return fmt.Errorf("copying: %w", err)
	}
	dstFile.Close()

	if err := os.Rename(tmp, dst); err != nil {
		os.Remove(tmp)
		return fmt.Errorf("renaming: %w", err)
	}

	return nil
}
