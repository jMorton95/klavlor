//go:build !windows

package install

import "fmt"

func createStartupEntry(exePath string) error {
	return fmt.Errorf("install is only supported on Windows")
}

func removeStartupEntry() error {
	return fmt.Errorf("install is only supported on Windows")
}

func copyExe(src, dst string) error {
	return fmt.Errorf("install is only supported on Windows")
}
