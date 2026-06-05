//go:build !windows && !darwin

package install

import "fmt"

func createStartupEntry(exePath string) error {
	return fmt.Errorf("auto-start is not supported on this platform")
}

func removeStartupEntry() error {
	return fmt.Errorf("auto-start is not supported on this platform")
}

func copyExe(src, dst string) error {
	return fmt.Errorf("install is not supported on this platform")
}
