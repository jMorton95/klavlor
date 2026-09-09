# The Light Theme Is The Dark Theme With The Neutrals Inverted

**Investigated 2026-09-08 against the running app** (`https://localhost:7081`, Chromium at
1600x1000, seeded sandbox data). Every figure below is measured from the live DOM, not estimated —
the method is recorded at the bottom so it can be re-run.

> **OUTCOME: THE LIGHT THEME WAS REMOVED on 2026-09-09.** This document is kept as the record of
> why, not as a live proposal.
>
> The parchment retune below was applied first and reproduced every figure it predicted — the whole
> "validated ladder" to two decimal places, dark mode bit-identical. It was then rejected on sight:
> *"there is so much contrasting with the colour scheme we have used"*, and the decision was to drop
> the light theme entirely rather than keep tuning it. The app is dark-only; see CLAUDE.md, "The App
> Is Dark-Only. There Is No Theme To Switch.", for what the removal did and how it was verified.
>
> **Read the diagnosis before proposing a second theme again.** That part outlives the proposal: the
> complaint was never really "the colours are too bright" (dimming buys ~11%, measured), it was that
> a neutral ramp mirrored from a dark theme cannot survive the trip — above 96.8% lightness there is
> no room left for an elevation ladder. A theme built the same way will fail the same way.

Status: **superseded.** Diagnosis retained; the proposal is no longer in effect.

## The complaint

> The lightmode UI for the entire website is eye burningly bright and borderline unusable.

It is, and the cause is more specific than "the colours are too bright".

## The diagnosis

There is no light theme. There is a dark theme whose neutral ramp was mirrored, and nothing else was
re-tuned for a light ground. Every symptom below follows from that one fact.

### 1. The elevation ladder was mirrored, and the mirror does not survive the trip

Dark mode pairs a `slate-900` panel on a `slate-950` page. Light mode pairs `slate-50` on
`slate-100` — the same relationship, reflected. In dark that is six lightness points of elevation.
Reflected into light there is no room left above 96.8%, so it buys 1.6.

| | dark | light | |
|---|---|---|---|
| card edge definition (`border` vs panel) | 1.72:1 | **1.18:1** | 4.1x less |
| card vs page ground (panel vs body) | 1.13:1 | **1.05:1** | 2.8x less |

At 1.05:1 a card does not read as a card. 74% of the feed viewport is panel and 26% is page ground,
and the two are separated by 1.05:1 — so in practice the whole viewport is a single uninterrupted
sheet at 96.8-98.4% lightness, and the only thing dividing it is a near-white hairline.

The admin section index is the clearest case: a mostly-empty page where the panel is invisible
against the ground and the section labels are ghosts.

### 2. The tier borders are `*-400` in **both** themes, so they are tuned for the wrong ground

`GetValueTierClasses` (`LootFeedItem.razor`) emits `border-amber-400`, `border-purple-400`,
`border-blue-400`, `border-green-400` with **no `dark:` pairing**. A 400 is chosen to glow against
near-black. On a 98%-white card the same value is a saturated 1px hairline.

Because the surfaces are not separating anything (point 1), those hairlines end up carrying *all* of
the page's structure. That is the shimmer. And they are not even doing it well:

| tier edge on the card | today | 3:1 UI-component threshold |
|---|---|---|
| legendary `amber-400` | 1.60:1 | fails |
| epic `purple-400` | 2.53:1 | fails |
| rare `blue-400` | 2.43:1 | fails |
| uncommon `green-400` | 1.67:1 | fails |

Saturated enough to shimmer, not contrasty enough to define. The worst of both.

### 3. The text ladder is the real reason it *hurts*

This is the finding that matters most, and it is the one that turns "bright" into "borderline
unusable".

| light-mode text on the card surface | uses | contrast |
|---|---|---|
| `text-slate-400` | 248 | **2.45:1** |
| `text-slate-500` | 292 | **4.55:1** |

`text-slate-400` and `text-slate-500` are the two most-used text colours in the entire app. Nearly
all of their uses render at 9-10px. WCAG's 4.5:1 threshold assumes ~16px; at 9px, 4.55:1 is
functionally unreadable and 2.45:1 is not text.

Measured failures against WCAG AA, light mode:

- **loot feed: 195 of 536 text elements** below AA
- **superiors: 151 of 264 text elements** below AA (57%)

The mechanism is the combination, not either half: the pupil constricts to a near-white full-screen
field, and then has to resolve 2.45:1 grey inside it. A bright page with strong text is merely
bright. A bright page with faint text is painful.

**This exact bug was already found and fixed on the dark side.** The `dark-mode-no-dim-grey-text`
note ("use `dark:`+`text-white` for small/secondary text, never dim slate-400/500") is the same
discovery. The light side has the same bug and never got the same rule.

### 4. The sidebar never got a light treatment at all

`SidebarComponent.razor` is `bg-linear-to-b from-slate-900 to-slate-800` with no `dark:` variant, in
both themes. Its inner items are `text-slate-400 hover:text-white hover:bg-slate-700`, also unpaired.
It is off-canvas so it is only transiently visible, but opening it in light mode puts a near-black
panel against a 97%-white page.

## One measurement that changes the recommendation

Dimming the surfaces is the obvious fix and it is **mostly not the fix**.

Area-weighted mean background luminance of the feed viewport, sampled on a 64x40 pixel grid through
`elementsFromPoint` (1.0 = a pure white screen):

| | mean luminance | |
|---|---|---|
| current | 0.881 | — |
| A, surface `#f2f0ec` | 0.836 | 5% less light |
| C, surface `#e9e5dc` | 0.810 | 8% less light |
| D, surface `#dfdbd2` | 0.787 | 11% less light |

Relative luminance saturates near white, so even a visibly grey-beige page buys ~11%. **The relief
has to come from contrast structure, not from dimming.** Any proposal sold on "this will be much
dimmer" is overselling; the surfaces come down a moderate amount and the structure does the work.

## The proposal — "parchment"

A **scoped variable override**, roughly 40 lines appended to `KlavLor.Web/wwwroot/app.css`, with
**zero `.razor` changes**.

### Why a variable override and not a semantic-token refactor

The first instinct is that this needs semantic tokens (`bg-surface`, `text-ink-muted`) across ~1,500
class occurrences, because **the slate ramp is shared by both themes at every stop**:

```
stops used in dark: variants     stops used bare (= the light values)
  slate-100  143                   slate-50   235
  slate-200  140                   slate-100  116
  slate-300  138                   slate-200  247
  ...                              ...
```

`dark:`+`text-slate-100` and `bg-slate-100` read the same variable, so retuning `--color-slate-100`
globally would rewrite dark-mode body text.

Scoping every override to `:root:not(.dark)` sidesteps this completely. A `dark:` utility only
applies when `.dark` is on `<html>` — which is precisely when the override stops applying. The two
can never both be in force.

**Verified, not assumed.** With `.dark` set, probing body / card / muted text / border colours and
the raw `--color-slate-*` values, with and without the block: **every value is bit-identical.** The
`@theme` variables still resolve to Tailwind's own `oklch()` figures.

The one thing to know before editing it: Tailwind emits `.bg-slate-50 { background-color:
var(--color-slate-50) }`, so the utilities genuinely read the variable. This would not work if
Tailwind inlined the literals.

### The block

```css
:root:not(.dark) {
    /* ---- surfaces ---- */
    --color-slate-50:  #e9e5dc;  /* card / panel          (was #f8fafc) */
    --color-slate-100: #ded9ce;  /* inset chip, th        (was #f1f5f9) */

    /* ---- edges. Dark gets 1.72:1 out of slate-700 on slate-900; light was
       getting 1.18:1, which is why nothing reads as a card. ---- */
    --color-slate-200: #b3ada0;  /* 1px card edge         (was #e2e8f0) */
    --color-slate-300: #97917f;  /* gridline / strong     (was #cbd5e1) */

    /* ---- ink. The mirror of the dark theme's "no dim grey text" rule. ---- */
    --color-slate-400: #585349;  /* tertiary   2.45:1 -> 6.08:1 */
    --color-slate-500: #4a463d;  /* secondary  4.55:1 -> 7.48:1 */
    --color-slate-600: #3f3b33;
    --color-slate-700: #35322b;
    --color-slate-800: #28251f;
    --color-slate-900: #1e1c18;  /* primary ink + the sidebar gradient */

    /* ---- tier accents: the stops that clear 3:1 against the card. ---- */
    --color-amber-400:  #b45309;  /* legendary  1.60:1 -> 3.99:1 */
    --color-purple-400: #9333ea;  /* epic       2.53:1 -> 4.28:1 */
    --color-blue-400:   #2563eb;  /* rare       2.43:1 -> 4.11:1 */
    --color-green-400:  #047857;  /* uncommon   1.67:1 -> 4.36:1 */

    /* ---- drop-value chip tints, deepened for the darker card ---- */
    --color-amber-100:  #fbeccd;
    --color-purple-100: #ece0fb;
    --color-blue-100:   #dbe6fd;
    --color-green-100:  #d6f2e2;
}

/* The page ground. bg-slate-100 serves two roles — the body ground and the
   inset chips — so the body takes the deeper step on its own element rather
   than through the token, which would drag every chip down with it. */
:root:not(.dark) body { background-color: #d5d0c5; }

/* Reserve white for what genuinely floats. It is the brightest value there is,
   so spending it on a full-page surface is what made the app glare; spending it
   on a 200px input is what makes the input look editable. */
:root:not(.dark) input,
:root:not(.dark) select,
:root:not(.dark) textarea { background-color: #ffffff; }

/* The superiors band draws its two horizontal rules itself, as pseudo-elements
   on .superior-band rather than as borders on either half (see CLAUDE.md). */
:root:not(.dark) .superior-band::before,
:root:not(.dark) .superior-band::after { background-color: #b3ada0; }
```

### The validated ladder

| structure | today | proposed | dark, for reference |
|---|---|---|---|
| card edge definition | 1.18:1 | **1.78:1** | 1.72:1 |
| card vs page ground | 1.05:1 | **1.22:1** | 1.13:1 |
| inset chip vs card | — | 1.12:1 | |
| input / popover vs card | — | 1.26:1 | |

| text on the card surface | today | proposed |
|---|---|---|
| `slate-900` primary | 17.06:1 | 13.53:1 |
| `slate-700` body | 9.90:1 | 10.17:1 |
| `slate-500` secondary | 4.55:1 | **7.48:1** |
| `slate-400` tertiary | 2.45:1 | **6.08:1** |

| worst-case grounds | proposed |
|---|---|
| tertiary on the page ground | 4.97:1 |
| tertiary on an inset chip | 5.43:1 |
| secondary on the page ground | 6.11:1 |

WCAG AA on the superiors page: **151/264 failing -> 40/264**. The 40 that remain are the off-canvas
sidebar, measured through its `translate-x-full` against the page ground; its own white-on-near-black
is fine. That is a measurement artefact of the audit, not 40 real failures — but see the open
question about the sidebar below.

### Why warm

Slate is a cool blue-grey ramp, and blue is the harshest part of a high-luminance field. Shifting the
light theme warm cuts perceived glare more than the measured luminance drop suggests, and it suits
the amber accent (`--color-main-100: #d97706`) and the game's own palette. A cool-neutral variant was
tested and measures within a point of parchment on every row above, so this is a taste call rather
than a contrast one.

## Alternatives considered and rejected

**Retuning the slate ramp globally, unscoped.** Rejected: the ramp is shared at every stop
(`dark:`+`text-slate-100` = 143 uses vs `bg-slate-100` = 116). Changing `--color-slate-200` for a
stronger border would rewrite dark-mode body text.

**A semantic-token refactor** (`bg-surface`, `text-ink-muted`, no `dark:` variants). Not rejected on
merit — it is the better end state, collapsing every `x dark:y` pair to one token and putting the
whole theme in one file. Rejected *for now* as the wrong first move: it is ~1,500 class occurrences
for the same visual result the scoped override gets in 40 lines. Worth doing later, on its own,
where it can be reviewed as a refactor rather than as a theme change.

**Keeping cards near-white and only darkening the ground.** Rejected by measurement: 74% of the feed
viewport is card, so moving only the ground changed the area-weighted mean by 4%.

**Selling this as a brightness fix.** Rejected by measurement — see the section above. It is a
contrast fix that also dims a little.

## Open questions before applying

- **`bg-slate-200` chips (17 uses) become `#b3ada0`.** That stop is doing double duty as both the
  card border and a chip fill; the value chosen is tuned for the border. The chips may read muddy and
  want their own treatment.
- **The sidebar.** Under this block it becomes a warm near-black rather than a blue one, which is
  consistent, but it still has no light treatment. Worth deciding whether it should get one or stay
  deliberately dark at every width.
- **Ground darkness is a taste call.** Candidate D (surface `#dfdbd2`, ground `#c9c4b8`) is a full
  step darker and also measured; parchment is the middle of three.
- Nothing here has been checked against the **charts** (`StackPalette`, the trend and histogram
  components) or the **canvas builder**, both of which paint their own fills. `StackPalette` is
  already documented as bright fills under dark text, so it likely needs no change, but it has not
  been looked at.

## How the figures were measured

All in-page, via Chromium devtools evaluation against the live app:

- **Colour resolution.** Tailwind 4 emits `oklch()`, which naive parsing reads as RGB and silently
  produces nonsense ratios. Every colour is resolved through a 1x1 canvas (`ctx.fillStyle = css`,
  then read the pixel back) so the browser does the conversion.
- **Effective background.** Walk ancestors accumulating backgrounds until one is opaque, then
  composite the stack back down over white. Necessary because most text sits on transparent elements,
  and several surfaces are alpha (`bg-slate-50/80` on the header).
- **Contrast.** WCAG 2.x relative luminance, threshold 4.5:1 normal / 3:1 large (>=24px, or >=18.66px
  at weight >=700).
- **Emitted light.** 64x40 grid over the viewport; at each point `elementsFromPoint` gives the paint
  stack, composited as above, and the luminances are averaged.
- **The dark-mode safety check.** Add `.dark` to `<html>`, toggle the override block's `textContent`
  between empty and full, wait 1200ms for `transition-colors` on `body` to settle, and compare probed
  values. **The settle matters** — a 350ms wait catches the body mid-transition and reports a false
  difference.

## What changed on the way in

Applied 2026-09-09. Two deviations from the block as written above, plus one finding the audit did
not have.

**1. The `.superior-band` rule was dropped as redundant.** The proposal ends with an explicit
`background-color: #b3ada0` on `.superior-band::before/::after`. Those pseudo-elements already read
`background: var(--color-slate-200)`, so the token override drives them on its own — verified in the
running app, the rules render `rgb(179, 173, 160)`. Restating the literal would have left a second
copy of the value to drift out of step the next time the edge is retuned.

**2. `bg-slate-200` got its own fill, which the audit had left as an open question.** It is not just
"may read muddy": the stop is both the 1px card edge and a fill for 30 occurrences (status badges,
the feed's luck pill, progress-bar tracks), and tuning it for the edge took the fill with it. On the
feed that put the luck multiple (`text-amber-600`, 10px, 94 of them on one screen) at **1.43:1 —
worse than the 2.2:1 it had before the retune**, i.e. a regression rather than a trade. So the token
stays tuned for the edge and the fill is `#d6d0c4`, one rung deeper than the slate-100 inset chip
(1.22:1 against the card, the same step as card-against-ground).

The one place `bg-slate-200` genuinely draws a rule — the session divider in `CharacterSessionList` —
keeps the edge value, selected without touching any `.razor`: **a background one pixel tall is a
rule, not a fill**, and Tailwind has already put `h-px` on the element. Note the `dark:` pairing is
*not* a usable discriminator here, which was the first thing tried: six of the seven
`dark:`+`bg-slate-700` pairings are chips, not rules.

**3. `slate-400` had the same split, and the audit's guess about the charts was wrong.** Open
question 4 says the charts "likely need no change" because `StackPalette` is bright fills under dark
text. That is precisely why they DO need one: `StackPalette.Other` is `bg-slate-400` carrying
`text-slate-900`, and so are `HistogramBars`' default segment and `DropTrendPanel`'s `OtherFill`.
Retuning the stop to tertiary **ink** put near-black text on a near-black block — measured
**6.63:1 -> 2.23:1**. A chart segment is a fill and `text-slate-400` is ink; they are one token and
only one of them wanted moving.

Fixed the same way as `slate-200`: `.bg-slate-400` gets `#a8a294`, the warm equivalent of Tailwind's
own `#94a3b8` measured on both axes `StackPalette` depends on — dark label on the fill 6.63 -> 6.69,
fill against the card 2.04 -> 2.02. The `/40` alpha variants are deliberately not caught, so
`TrendLineChart`'s hover crosshair stays the ink value, which on a light ground is the right way
round.

The general form of this trap, worth stating once: **the ink retune breaks any `slate-400`+ stop
used as a background.** Audited — `bg-slate-500`/`600` are only ever `dark:`-prefixed, and unprefixed
`bg-slate-700/800/900` were dark fills under white text in both themes already and merely became
warm.

**4. Open, and NOT fixed: `text-amber-600` is a third dim-ink case the audit missed.** It is 138
uses, and on a light chip it cannot clear AA at any chip fill — measured across five candidate
fills it tops out at **2.21:1**. The chip fix above restores it to parity with its pre-change value
and no further; it was already failing before any of this. Fixing it properly means retuning
`--color-amber-600` under `:root:not(.dark)` the same way the tier accents were retuned, which is a
brand-accent decision across 138 text + 22 background + 12 border uses and deserves to be made
deliberately rather than folded into this change.

### Re-measured after applying

Every figure in "The validated ladder" reproduced exactly: card edge **1.78:1**, card vs ground
**1.22:1**, `slate-400` **6.08:1**, `slate-500` **7.48:1**, tertiary on ground **4.97:1**, secondary
on ground **6.11:1**, and the tier accents at **3.99 / 4.28 / 4.11 / 4.36** — all to two decimals.
Superiors went **151/264 below AA to 75/583**, of which 12 are the off-canvas sidebar artefact the
audit describes. Feed viewport mean luminance measured **0.754**.

Dark mode was re-probed with `.dark` set and the transition allowed to settle: every
`--color-slate-*` and `--color-{amber,purple,blue,green}-*` still resolves to Tailwind's own
`oklch()`, and body / card / border / chip / input all resolve to their original slate stops. The
chip and white-input rules do not leak. 312 unit tests and 20 JS tests pass.

The remaining dimmest thing on the superiors page is the `text-slate-300` "never on task" dash at
2.24:1, which is deliberate de-emphasis per CLAUDE.md rather than a defect.
