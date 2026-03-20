//go:build windows

package install

import (
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

const vbsFilename = "klavlor-sync.vbs"

func startupDir() string {
	return filepath.Join(os.Getenv("APPDATA"), "Microsoft", "Windows", "Start Menu", "Programs", "Startup")
}

func vbsPath() string {
	return filepath.Join(startupDir(), vbsFilename)
}

func createStartupEntry(exePath string) error {
	// VBS uses doubled quotes inside strings to produce literal quotes.
	escaped := strings.ReplaceAll(exePath, `"`, `""`)
	script := fmt.Sprintf(`CreateObject("WScript.Shell").Run """%s"" --background", 0`, escaped)

	dir := startupDir()
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return fmt.Errorf("creating startup directory: %w", err)
	}

	return os.WriteFile(vbsPath(), []byte(script), 0o644)
}

func removeStartupEntry() error {
	err := os.Remove(vbsPath())
	if os.IsNotExist(err) {
		return nil
	}
	return err
}

func copyExe(src, dst string) error {
	// Normalize paths so comparison works even with mixed separators.
	srcAbs, _ := filepath.Abs(src)
	dstAbs, _ := filepath.Abs(dst)
	if strings.EqualFold(srcAbs, dstAbs) {
		return nil // already in place
	}

	// Kill any running instance (ignore errors — may not be running).
	_ = exec.Command("taskkill", "/F", "/IM", filepath.Base(dst)).Run()

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
