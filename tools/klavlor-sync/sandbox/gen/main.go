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
	"bufio"
	"bytes"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/klavlor/klavlor-sync/internal/model"
)

// dropSpec is one item line within a kill.
type dropSpec struct {
	name           string
	id, qty, price int
}

// source is a fake monster with a fixed drop table. Prices are chosen so the
// set spans several loot-feed tiers (Standard 10K+, Rare 1M+, Epic 10M+).
var sources = []struct {
	name       string
	level      int
	drops      []dropSpec
	oneAtATime bool
}{
	{name: "Sandbox Goblin", level: 2, drops: []dropSpec{
		{"Coins", 995, 120, 1},
		{"Bronze dagger", 1205, 1, 35},
	}},
	{name: "Sandbox Zulrah", level: 725, drops: []dropSpec{
		{"Zulrah's scales", 12934, 350, 250}, // ~87K — Standard
		{"Magic fang", 12932, 1, 2_500_000},  // 2.5M — Rare
	}},
	{name: "Sandbox Vorkath", level: 732, drops: []dropSpec{
		{"Superior dragon bones", 22124, 30, 1500},
		{"Draconic visage", 11286, 1, 11_000_000}, // 11M — Epic
	}},
	// One untradeable at 0 GP, exactly like an Unsired hand-in: RuneLite reports no
	// price, so it only reaches a lane at all once an admin item-value override is
	// set. Its whole point is to exercise the override path on the LIVE publish,
	// where the price comes from DropsJson rather than the stored projection.
	// An Unsired hand-in returns ONE piece, a different one each time, and each is
	// untradeable so RuneLite reports 0 GP - they only reach a lane at all once an
	// admin item-value override is set. oneAtATime makes the generator emit a single
	// rotating drop per kill rather than the whole table, which is what produces a
	// card whose chip LIST grows rather than one chip whose quantity climbs.
	{name: "Sandbox Unsired", level: 350, drops: []dropSpec{
		{"Sandbox bludgeon claw", 26476, 1, 0},
		{"Sandbox bludgeon spine", 26477, 1, 0},
		{"Sandbox bludgeon axon", 26478, 1, 0},
		{"Sandbox abyssal dagger", 26479, 1, 0},
		{"Sandbox abyssal head", 26480, 1, 0},
	}, oneAtATime: true},
}

func main() {
	dir := flag.String("dir", "", "loots root directory (required)")
	character := flag.String("character", "SANDBOX", "character subdirectory (RuneLite account name)")
	count := flag.Int("count", 6, "number of kills to write across the source rotation")
	appendMode := flag.Bool("append", false, "append to existing logs instead of truncating (simulates new live kills)")
	offset := flag.Int("offset", 0, "start the source rotation here, so successive one-kill appends hit different sources")
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
	//
	// In append mode the count CONTINUES from what is already in the log. Restarting it
	// at 1 per invocation made every appended kill claim "KC 1", which the server takes at
	// face value - RuneLite is the authority on kill count - so a dripped stream showed a
	// column of "#1" on the roll ticker instead of a counter climbing.
	kc := map[string]int{}
	if *appendMode {
		for _, src := range sources {
			kc[src.name] = countLines(filepath.Join(charDir, src.name+".log"))
		}
	}
	files := map[string]*os.File{}
	defer func() {
		for _, f := range files {
			f.Close()
		}
	}()

	written := 0
	for i := 0; i < *count; i++ {
		src := sources[(i+*offset)%len(sources)]
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

		var drops []model.LootDrop
		if src.oneAtATime {
			d := src.drops[(kc[src.name]-1)%len(src.drops)]
			drops = []model.LootDrop{{Name: d.name, Id: d.id, Quantity: d.qty, Price: d.price}}
		} else {
			drops = make([]model.LootDrop, len(src.drops))
			for j, d := range src.drops {
				drops[j] = model.LootDrop{Name: d.name, Id: d.id, Quantity: d.qty, Price: d.price}
			}
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

// countLines returns how many kills a source's log already holds - one JSON record per
// line. A missing or unreadable log counts as zero, which is the right answer for a
// source this generation has not written yet.
func countLines(path string) int {
	f, err := os.Open(path)
	if err != nil {
		return 0
	}
	defer f.Close()

	n := 0
	scanner := bufio.NewScanner(f)
	scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
	for scanner.Scan() {
		if len(bytes.TrimSpace(scanner.Bytes())) > 0 {
			n++
		}
	}
	return n
}
