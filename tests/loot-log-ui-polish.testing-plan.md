# Loot Log & Feed UI Polish — Playwright MCP Testing Plan

**App URL:** `https://localhost:7081`
**Routes under test:**
- `/loot/log` — All users list
- `/loot/log/{userId}` — User loot log (source cards grid)
- `/loot/log/{userId}/source?name={sourceName}` — Source detail (kills + aggregated drops)

**Prerequisites:**
1. PostgreSQL running (`docker compose up -d`)
2. App running (`npm run dev` or `dotnet run --project KlavLor.Web`)
3. At least one user with loot data (multiple sources, >20 sources for pagination, one source with >25 kills and >5 distinct drop types)

---

## Test 1: Show More Button Below Grid

**Goal:** Verify the "Show More" button renders outside/below the CSS grid, not as a grid child.

### Steps

```
1. browser_navigate → https://localhost:7081/loot/log
2. browser_click → first user link to open their loot log
3. browser_take_screenshot → capture full page
```

### Assertions (visual)

- [ ] Source cards render in a multi-column grid (2-5 cols depending on viewport)
- [ ] "Show More" button is centered BELOW the grid, not occupying a grid cell
- [ ] There is no empty grid cell where the button used to be

### Steps (continued — click Show More)

```
4. browser_click → "Show More" button
5. browser_take_screenshot → capture after load
```

### Assertions (visual)

- [ ] New source cards appended into the grid (grid grew)
- [ ] "Show More" button still appears below the grid (if more pages remain)
- [ ] OR "Show More" disappears entirely (if no more pages)
- [ ] No duplicate cards visible

---

## Test 2: Dark Mode Text Readability

**Goal:** All text on source cards, kill entries, feed items, and aggregated drops is legible in dark mode.

### Steps

```
1. browser_navigate → https://localhost:7081/loot/log
2. browser_click → user link
3. browser_take_screenshot → light mode baseline
4. browser_click → dark mode toggle (or execute JS: document.documentElement.classList.add('dark'))
5. browser_take_screenshot → dark mode source cards
```

### Assertions (visual — dark mode screenshot)

- [ ] Source card titles (h3) are bright white/near-white (`text-slate-100`)
- [ ] Item names in top drops list are clearly readable on dark card background
- [ ] Quantity text (e.g. "x28") is visible, not blending into the background
- [ ] Gold values (amber text) remain clearly visible
- [ ] Source type badges (Npc/Event/etc) have readable text on their colored backgrounds

### Steps (continued — source detail in dark mode)

```
6. browser_click → any source card to open detail
7. browser_take_screenshot → dark mode source detail
```

### Assertions (visual)

- [ ] "All Drops (aggregated)" items have readable names on `dark:bg-slate-800` pills
- [ ] Quantity text in drop pills is visible (`dark:text-slate-400`, not `dark:text-slate-500`)
- [ ] Kill log entries: timestamp text is readable
- [ ] Kill log entries: drop badge text is clearly visible (`dark:text-slate-200`)
- [ ] Kill log entries: price text in parentheses is readable (`dark:text-slate-400`)

### Steps (continued — feed page in dark mode)

```
8. browser_navigate → https://localhost:7081/loot/feed (or wherever the feed lives)
9. browser_take_screenshot → dark mode feed
```

### Assertions (visual)

- [ ] Feed item drop badges have readable text (`dark:text-slate-200`)
- [ ] "killed"/"rolled" action text is visible (`dark:text-slate-400`)
- [ ] Timestamp text is visible (`dark:text-slate-400`)
- [ ] Price text in drop badges is readable

---

## Test 3: Gap Between Item Name and Quantity

**Goal:** Verify spacing between item name and quantity (e.g. "Corrupted shards x28" not "Corrupted shardsx28").

### Steps

```
1. browser_navigate → https://localhost:7081/loot/log
2. browser_click → user with loot data
3. browser_take_screenshot → zoom into a source card with multi-quantity drops
```

### Assertions (visual)

- [ ] Source card top drops show a visible gap: "Item Name x123" (space before x)
- [ ] The `ml-1` margin creates clear separation between name and quantity

### Steps (continued — source detail aggregated drops)

```
4. browser_click → source card to open detail
5. browser_take_screenshot → "All Drops (aggregated)" section
```

### Assertions (visual)

- [ ] Each drop pill shows "Item Name x123" with spacing
- [ ] No items appear as "ItemNamex123" (concatenated)

---

## Test 4: Kill Log Grid Layout

**Goal:** Kill log entries use a responsive grid (not full-width vertical stack).

### Steps

```
1. browser_navigate → https://localhost:7081/loot/log/{userId}/source?name={sourceName}
   (pick a source with multiple kills)
2. browser_take_screenshot → full width viewport (1280px+)
```

### Assertions (visual)

- [ ] Kill log entries render in a multi-column grid (up to 4 columns on xl)
- [ ] Entries flow left-to-right, top-to-bottom
- [ ] Most recent kill is top-left
- [ ] Each entry is a compact card with border and rounded corners
- [ ] No entry spans full width (unless viewport is mobile-narrow)

### Steps (continued — responsive check)

```
3. browser_navigate → same URL (resize viewport or use mobile emulation if available)
4. browser_take_screenshot → narrow viewport (~375px)
```

### Assertions (visual)

- [ ] At mobile width, kill entries stack to single column (`grid-cols-1`)
- [ ] At medium width (~768px), 2 columns
- [ ] At large width (~1024px), 3 columns

---

## Test 5: Relative Timestamps on Kill Entries

**Goal:** Kill entries show "Xh Ym ago" for entries <24h old, absolute date otherwise.

### Steps

```
1. browser_navigate → source detail page for a source with recent kills (<24h)
2. browser_take_screenshot → capture kill log section
```

### Assertions (visual)

- [ ] Recent kills (within last hour) show "Xm ago" format
- [ ] Kills within last 24 hours show "Xh Ym ago" format (e.g. "3h 15m ago")
- [ ] Kills older than 24 hours show "MMM dd, HH:mm" format (e.g. "Mar 19, 14:30")
- [ ] "just now" appears for very recent kills (<1 min)

> **Note:** If no recent kills exist in the database, you may need to trigger a loot sync or manually verify by checking timestamps against current time.

---

## Test 6: All Drops (Aggregated) — Full Drop Table

**Goal:** Source detail shows ALL aggregated drops, not just top 5.

### Steps

```
1. browser_navigate → source detail for a source known to have >5 distinct drops
   e.g. https://localhost:7081/loot/log/{userId}/source?name=Corrupted%20Hunllef
2. browser_take_screenshot → "All Drops (aggregated)" section
```

### Assertions (visual)

- [ ] More than 5 drop types are displayed in the grid
- [ ] Drops are ordered by total value (descending)
- [ ] Grid uses responsive columns (2 → 3 → 4 → 5 based on viewport width)
- [ ] All drops have name, quantity (if >1), and gold value

### Steps (comparison with source card)

```
3. browser_navigate → back to user log (click "Back to Sources")
4. browser_take_screenshot → source card for same source
```

### Assertions (visual)

- [ ] Source card preview still shows only top 5 drops (not all)
- [ ] Source detail shows the full list

---

## Test 7: Show More on Kill Log (Source Detail)

**Goal:** Kill log pagination works with the new grid + OOB swap pattern.

### Steps

```
1. browser_navigate → source detail for a source with >25 kills
2. browser_take_screenshot → initial kill log grid + show more button
3. browser_click → "Show More" button below kill grid
4. browser_take_screenshot → after loading more kills
```

### Assertions (visual)

- [ ] "Show More" button appears below the kill grid, not inside it
- [ ] After clicking, new kill entries append into the grid
- [ ] Grid maintains its column layout with the new entries
- [ ] "Show More" updates with next page URL (or disappears if no more)
- [ ] No layout shift or broken grid

---

## Test 8: End-to-End Dark Mode Walkthrough

**Goal:** Full walkthrough in dark mode hitting all changed components.

### Steps

```
1. browser_navigate → https://localhost:7081/loot/log
2. Enable dark mode
3. browser_click → user → browser_take_screenshot (source cards grid)
4. browser_click → "Show More" → browser_take_screenshot (pagination works, button below grid)
5. browser_click → source card → browser_take_screenshot (source detail)
   - Verify: all drops section, kill log grid, relative timestamps
6. browser_click → "Show More" on kills → browser_take_screenshot (kill pagination)
7. Navigate to feed → browser_take_screenshot (feed items dark mode)
```

### Assertions (visual — cumulative)

- [ ] No invisible or near-invisible text anywhere in the flow
- [ ] All interactive elements (buttons, links, cards) have visible text and borders
- [ ] Hover states on cards don't make text disappear
- [ ] Gold/amber values are consistently visible across all views

---

## Quick Reference: CSS Class Changes to Verify

| Component | Element | Old Dark Class | New Dark Class |
|-----------|---------|---------------|----------------|
| LootSourceCard | Item name | `dark:text-slate-300` | `dark:text-slate-100` |
| LootSourceCard | Quantity | `dark:text-slate-500` | `dark:text-slate-400` |
| LootLogSourceDetail | Drop name | `dark:text-slate-300` | `dark:text-slate-100` |
| LootLogSourceDetail | Quantity | `dark:text-slate-500` | `dark:text-slate-400` |
| LootLogKillEntry | Timestamp | `dark:text-slate-500` | `dark:text-slate-400` |
| LootLogKillEntry | Drop badge | `dark:text-slate-300` | `dark:text-slate-200` |
| LootLogKillEntry | Price | `dark:text-slate-500` | `dark:text-slate-400` |
| LootFeedItem | Drop badge | `dark:text-slate-300` | `dark:text-slate-200` |
| LootFeedItem | Price | `dark:text-slate-500` | `dark:text-slate-400` |
| LootFeedItem | Action text | `dark:text-slate-500` | `dark:text-slate-400` |
| LootFeedItem | Timestamp | `dark:text-slate-500` | `dark:text-slate-400` |
