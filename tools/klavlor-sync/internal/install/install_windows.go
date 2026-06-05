//go:build windows

package install

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
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
	// Same path → nothing to do (avoids taskkilling ourselves on a re-run in place).
	srcAbs, _ := filepath.Abs(src)
	dstAbs, _ := filepath.Abs(dst)
	if strings.EqualFold(srcAbs, dstAbs) {
		return nil
	}

	// Kill any previously-installed instance (ignore errors — may not be running).
	// Exclude our own PID so the installer doesn't kill itself.
	_ = exec.Command("taskkill", "/F", "/IM", filepath.Base(dst),
		"/FI", "PID ne "+strconv.Itoa(os.Getpid())).Run()

	return atomicCopy(src, dst)
}
