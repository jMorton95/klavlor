// Command gen writes fake RuneLite Loot Tracker .log files for sandbox testing
// of klavlor-sync. Each line is one kill in the exact JSON shape the tailer
// parses (model.LootRecord minus the fields the syncer fills in itself).
//
// Files are written to <dir>/<character>/<Source>.log, mirroring RuneLite's
// per-account layout (the syncer derives characterId from the first path
// segment). Source names are prefixed "Sandbox " so they never collide with
// real local loot data and are trivial to clean up.
//
//	go run ./sandbox/gen -dir /path/to/loots -count 6
//	go run ./sandbox/gen -dir /path/to/loots -count 3 -append   # add live kills
package main

import (
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/klavlor/klavlor-sync/internal/model"
)

// dropSpec is one item line within a kill.
type dropSpec struct{ name string; id, qty, price int }

// source is a fake monster with a fixed drop table. Prices are chosen so the
// set spans several loot-feed tiers (Standard 10K+, Rare 1M+, Epic 10M+).
var sources = []struct {
	name  string
	level int
	drops []dropSpec
}{
	{"Sandbox Goblin", 2, []dropSpec{
		{"Coins", 995, 120, 1},
		{"Bronze dagger", 1205, 1, 35},
	}},
	{"Sandbox Zulrah", 725, []dropSpec{
		{"Zulrah's scales", 12934, 350, 250},   // ~87K — Standard
		{"Magic fang", 12932, 1, 2_500_000},    // 2.5M — Rare
	}},
	{"Sandbox Vorkath", 732, []dropSpec{
		{"Superior dragon bones", 22124, 30, 1500},
		{"Draconic visage", 11286, 1, 11_000_000}, // 11M — Epic
	}},
}

func main() {
	dir := flag.String("dir", "", "loots root directory (required)")
	character := flag.String("character", "SANDBOX", "character subdirectory (RuneLite account name)")
	count := flag.Int("count", 6, "number of kills to write across the source rotation")
	appendMode := flag.Bool("append", false, "append to existing logs instead of truncating (simulates new live kills)")
	flag.Parse()

	if *dir == "" {
		fmt.Fprintln(os.Stderr, "error: -dir is required")
		os.Exit(2)
	}

	charDir := filepath.Join(*dir, *character)
	if err := os.MkdirAll(charDir, 0o755); err != nil {
		fmt.Fprintf(os.Stderr, "error: creating %s: %v\n", charDir, err)
		os.Exit(1)
	}

	flags := os.O_CREATE | os.O_WRONLY | os.O_APPEND
	if !*appendMode {
		flags |= os.O_TRUNC
	}

	// Spread kills backwards from "now" so each generation produces unique
	// timestamps (and therefore unique content hashes server-side).
	base := time.Now().Add(-time.Duration(*count) * time.Minute)

	// Per-source running kill counts, plus open file handles created lazily.
	kc := map[string]int{}
	files := map[string]*os.File{}
	defer func() {
		for _, f := range files {
			f.Close()
		}
	}()

	written := 0
	for i := 0; i < *count; i++ {
		src := sources[i%len(sources)]
		kc[src.name]++

		f, ok := files[src.name]
		if !ok {
			path := filepath.Join(charDir, src.name+".log")
			var err error
			f, err = os.OpenFile(path, flags, 0o644)
			if err != nil {
				fmt.Fprintf(os.Stderr, "error: opening %s: %v\n", path, err)
				os.Exit(1)
			}
			files[src.name] = f
		}

		drops := make([]model.LootDrop, len(src.drops))
		for j, d := range src.drops {
			drops[j] = model.LootDrop{Name: d.name, Id: d.id, Quantity: d.qty, Price: d.price}
		}

		rec := model.LootRecord{
			Name:      src.name,
			Level:     src.level,
			KillCount: kc[src.name],
			Type:      "Npc",
			Drops:     drops,
			// Format must match the server's accepted layouts:
			// "MMM d, yyyy, h:mm:ss tt" e.g. "Jan 3, 2026, 3:45:30 PM".
			Date: base.Add(time.Duration(i) * time.Minute).Format("Jan 2, 2006, 3:04:05 PM"),
		}

		line, err := json.Marshal(rec)
		if err != nil {
			fmt.Fprintf(os.Stderr, "error: marshaling record: %v\n", err)
			os.Exit(1)
		}
		if _, err := f.Write(append(line, '\n')); err != nil {
			fmt.Fprintf(os.Stderr, "error: writing record: %v\n", err)
			os.Exit(1)
		}
		written++
	}

	mode := "wrote"
	if *appendMode {
		mode = "appended"
	}
	fmt.Printf("%s %d kills across %d source file(s) under %s\n", mode, written, len(files), charDir)
}
