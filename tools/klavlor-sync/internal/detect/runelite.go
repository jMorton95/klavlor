package detect

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// FindLootsDir locates the RuneLite loots directory.
// It checks the default location first, then scans one level deep in the
// home directory, and finally prompts the user for a manual path.
func FindLootsDir() (string, error) {
	home, err := os.UserHomeDir()
	if err != nil {
		return "", fmt.Errorf("cannot determine home directory: %w", err)
	}

	// 1. Check default location.
	defaultPath := filepath.Join(home, ".runelite", "loots")
	if isDir(defaultPath) {
		return defaultPath, nil
	}

	// 2. Scan one level deep in home dir for .runelite/loots.
	entries, err := os.ReadDir(home)
	if err == nil {
		for _, e := range entries {
			if !e.IsDir() || !strings.HasPrefix(e.Name(), ".runelite") {
				continue
			}
			candidate := filepath.Join(home, e.Name(), "loots")
			if isDir(candidate) {
				return candidate, nil
			}
		}
	}

	// 3. Prompt user.
	return promptForPath()
}

func promptForPath() (string, error) {
	reader := bufio.NewReader(os.Stdin)
	fmt.Println("  Could not auto-detect RuneLite loots directory.")
	fmt.Print("  Paste the full path to your RuneLite loots folder: ")

	input, err := reader.ReadString('\n')
	if err != nil {
		return "", fmt.Errorf("reading input: %w", err)
	}
	input = strings.TrimSpace(input)

	if input == "" {
		return "", fmt.Errorf("no path provided")
	}

	if !isDir(input) {
		return "", fmt.Errorf("directory does not exist: %s", input)
	}

	return input, nil
}

func isDir(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.IsDir()
}
