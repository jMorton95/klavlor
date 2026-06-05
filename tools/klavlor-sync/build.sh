#!/usr/bin/env bash
# Cross-compile klavlor-sync release binaries into ./dist.
#
# Produces:
#   dist/klavlor-sync.exe      Windows (amd64)
#   dist/klavlor-sync-macos    macOS universal (arm64 + amd64)  — only when `lipo`
#                              is available (i.e. run on macOS). Otherwise the two
#                              per-arch builds are left in dist/ unmerged.
#
# A Mac user building for their own machine doesn't need this script — just run
#   go build -o klavlor-sync .
# which builds natively for the local architecture. The universal binary only
# matters when distributing one prebuilt file to both Intel and Apple-Silicon Macs.
set -euo pipefail

cd "$(dirname "$0")"
mkdir -p dist
export CGO_ENABLED=0  # pure-Go tool, no cgo — keeps cross-compiles self-contained

echo "Building Windows (amd64)..."
GOOS=windows GOARCH=amd64 go build -o dist/klavlor-sync.exe .

echo "Building macOS (arm64)..."
GOOS=darwin GOARCH=arm64 go build -o dist/klavlor-sync-macos-arm64 .

echo "Building macOS (amd64)..."
GOOS=darwin GOARCH=amd64 go build -o dist/klavlor-sync-macos-amd64 .

if command -v lipo >/dev/null 2>&1; then
  echo "Merging universal macOS binary with lipo..."
  lipo -create -output dist/klavlor-sync-macos \
    dist/klavlor-sync-macos-arm64 dist/klavlor-sync-macos-amd64
  rm -f dist/klavlor-sync-macos-arm64 dist/klavlor-sync-macos-amd64
  echo "  -> dist/klavlor-sync-macos (universal)"
else
  echo "NOTE: lipo not found (not on macOS) — left per-arch macOS binaries:"
  echo "      dist/klavlor-sync-macos-arm64, dist/klavlor-sync-macos-amd64"
  echo "      Run this script on a Mac to produce a single universal binary."
fi

echo "Done. Artifacts in ./dist"
