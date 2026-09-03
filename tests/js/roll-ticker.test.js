// Tests for the loot feed's roll ticker (KlavLor.Web/wwwroot/roll-ticker.js).
//
// Every behaviour pinned here has already shipped broken once, which is the reason the file exists:
// the queue was added to stop a batch of rolls sliding in as one block and STILL did, because the
// gate was "is the queue empty" rather than the clock; and the reconnect dedupe was described in
// LootRollEntry.DomId's own doc comment for months while nothing implemented it. Both failures show
// chips - they are not crashes - so nothing but an assertion catches them.
//
// jsdom, not a browser: the slide is CSS and is not under test here. What is under test is the
// pacing, the ordering, the dedupe and the trim, all of which are pure DOM.

const test = require('node:test');
const assert = require('node:assert');
const { JSDOM } = require('jsdom');

const { createRollTicker } = require('../../KlavLor.Web/wwwroot/roll-ticker.js');

// A release happens in two hops: the MutationObserver callback is a microtask, and the release it
// schedules is a timer. One setTimeout is not enough to see the result - the test's own timer is
// created BEFORE the observer has run, so it fires first. Two hops makes the ordering deterministic.
const tick = async (ms = 0) => {
    await new Promise(r => setTimeout(r, 0));
    await new Promise(r => setTimeout(r, ms));
};

// Fast enough that the suite runs in a couple of seconds, slow enough to sit well clear of timer
// granularity - Date.now() on Windows can jump several milliseconds, and a cadence near that is a
// coin toss rather than a test. The production cadence is asserted against app.css separately (see
// the last test).
const SLIDE_MS = 60;
const GAP_MS = 20;
const CADENCE = SLIDE_MS + GAP_MS;

function harness({ maxChips = 40, slideMs = SLIDE_MS, gapMs = GAP_MS, maxQueued = 40 } = {}) {
    const dom = new JSDOM('<!doctype html><body><div id="roll-ticker"></div></body>');
    const { document } = dom.window;
    const track = document.getElementById('roll-ticker');
    track.dataset.maxChips = String(maxChips);

    // jsdom has no layout, so a chip would measure 0 and the slide would be skipped. Give every
    // chip a width so the transform path runs and can be asserted on.
    const CHIP_WIDTH = 100;
    Object.defineProperty(dom.window.HTMLElement.prototype, 'offsetWidth', { get: () => CHIP_WIDTH });

    const chip = (id, { seed = false } = {}) => {
        const el = document.createElement('span');
        el.id = id;
        el.className = 'roll-chip';
        if (seed) el.setAttribute('data-seed', 'true');
        return el;
    };

    // Exactly what htmx's sse extension does per frame: hx-swap="afterbegin" on the track.
    const arrive = (...els) => els.forEach(el => track.insertAdjacentElement('afterbegin', el));

    // jsdom has no way to make a document hidden, so visibility is injected. The production
    // default reads document.visibilityState; this stands in for it and lets a test drive the
    // same code path the browser would.
    const visibility = { hidden: false };
    const setHidden = async (value) => {
        visibility.hidden = value;
        document.dispatchEvent(new dom.window.Event('visibilitychange'));
        await tick(0);
    };

    const ticker = createRollTicker(track, {
        slideMs, gapMs, maxQueued, reducedMotion: () => false,
        isHidden: () => visibility.hidden,
    });

    return {
        dom, track, ticker, chip, arrive, CHIP_WIDTH, setHidden,
        ids: () => [...track.children].map(c => c.id),
        shift: () => {
            const m = /translateX\((-?[\d.]+)px\)/.exec(track.style.transform || '');
            return m ? parseFloat(m[1]) : 0;
        },
        settle: tick,
    };
}

test('a batch of rolls is released one at a time, not slid in as one block', async () => {
    const h = harness();

    // Five kills landing in one sync - five SSE frames in one task.
    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'), h.chip('d'), h.chip('e'));

    await h.settle(0);
    assert.strictEqual(h.ids().length, 1, 'only the first roll may be on screen after the batch');
    assert.strictEqual(h.ticker.pending(), 4, 'the rest wait in the queue');

    await h.settle(CADENCE * 6);
    assert.strictEqual(h.ids().length, 5, 'every roll arrives eventually');
    assert.strictEqual(h.ticker.pending(), 0);
});

test('a roll landing just after another still waits its turn', async () => {
    // THE REGRESSION. The first queue drained itself and then let the next arrival through
    // immediately, so two rolls a millisecond apart still stacked into one slide.
    const h = harness();

    h.arrive(h.chip('first'));
    await h.settle(0);
    assert.strictEqual(h.ids().length, 1);

    h.arrive(h.chip('second'));
    await h.settle(0);
    assert.strictEqual(h.ids().length, 1, 'the second roll must not jump the cadence');
    assert.strictEqual(h.ticker.pending(), 1);

    await h.settle(CADENCE * 2);
    assert.deepStrictEqual(h.ids(), ['second', 'first']);
});

test('each slide starts exactly one chip back, never a multiple', async () => {
    // The observable symptom of a batching regression: the track jumps several chip widths at once.
    //
    // slide() writes the start offset and then the target in the same task, so by the time a test
    // can look, the inline transform is already the target (0px) and jsdom has no transition to
    // interpolate. Watch the style attribute instead - the start offset is the previous value of
    // the write that sets it back to zero.
    const h = harness();
    const starts = [];

    new h.dom.window.MutationObserver(records => {
        for (const r of records) {
            const m = /translateX\((-?[\d.]+)px\)/.exec(r.oldValue || '');
            if (m && parseFloat(m[1]) !== 0) starts.push(parseFloat(m[1]));
        }
    }).observe(h.track, { attributes: true, attributeFilter: ['style'], attributeOldValue: true });

    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'));
    await h.settle(CADENCE * 5);

    assert.strictEqual(h.ids().length, 3, 'all three landed');
    assert.ok(starts.length >= 3, 'a slide was recorded for each release');
    // slide() touches the style attribute more than once per release, so assert the distinct set:
    // a batch would put -200 or -300 in here alongside the -100s.
    assert.deepStrictEqual([...new Set(starts)], [-h.CHIP_WIDTH],
        'every slide covers exactly one chip width - a multiple means a batch went in together');
});

test('rolls keep arrival order, and the row ends up as htmx would have left it', async () => {
    const h = harness();

    h.arrive(h.chip('oldest'), h.chip('middle'), h.chip('newest'));
    await h.settle(CADENCE * 5);

    // afterbegin means newest-leftmost. Pacing must not reorder anything.
    assert.deepStrictEqual(h.ids(), ['newest', 'middle', 'oldest']);
});

test('backfill lands immediately and in place, not through the queue', async () => {
    // The connect replay is history. Forty chips paced at one per 750ms would take half a minute
    // to draw the banner, and would animate as if forty kills had just happened.
    const h = harness();

    h.arrive(h.chip('s1', { seed: true }), h.chip('s2', { seed: true }), h.chip('s3', { seed: true }));
    await h.settle(0);

    assert.strictEqual(h.ids().length, 3, 'seed chips are not queued');
    assert.strictEqual(h.ticker.pending(), 0);
    assert.strictEqual(h.shift(), 0, 'and they do not slide');
});

test('a reconnect that replays the ring adds no duplicates', async () => {
    // An EventSource reconnects on any blip and the server answers with its whole ring. Reproduced
    // 10 duplicated chips before the dedupe existed.
    const h = harness();

    h.arrive(...['r1', 'r2', 'r3', 'r4'].map(id => h.chip(id, { seed: true })));
    await h.settle(0);
    const before = h.ids();

    // Replayed oldest-first, exactly as StreamRolls sends them.
    h.arrive(...[...before].reverse().map(id => h.chip(id, { seed: true })));
    await h.settle(0);

    assert.deepStrictEqual(h.ids(), before, 'the banner is unchanged by a replay');
    assert.strictEqual(new Set(h.ids()).size, h.ids().length);
});

test('a replay cannot duplicate a roll still waiting in the queue', async () => {
    // The window my own change opened: a live roll queued but not yet released, then the same roll
    // replayed as backfill by a reconnect, then the queued one released on top of it.
    const h = harness();

    h.arrive(h.chip('live1'), h.chip('live2'));
    await h.settle(0);
    assert.strictEqual(h.ticker.pending(), 1, 'live2 is queued, not on screen');

    h.arrive(h.chip('live2', { seed: true }));
    await h.settle(CADENCE * 3);

    assert.deepStrictEqual(h.ids(), ['live2', 'live1']);
    assert.strictEqual(new Set(h.ids()).size, h.ids().length);
});

test('the DOM is capped at data-max-chips', async () => {
    const h = harness({ maxChips: 3 });

    h.arrive(...['a', 'b', 'c', 'd', 'e'].map(id => h.chip(id, { seed: true })));
    await h.settle(0);

    assert.strictEqual(h.ids().length, 3);
    assert.deepStrictEqual(h.ids(), ['e', 'd', 'c'], 'the oldest are trimmed off the right');
});

test('a roll trimmed off the end can legitimately come back', async () => {
    // The dedupe must track what is ON SCREEN, not everything ever seen: an unbounded set would
    // leak, and a roll pushed off the end is no longer a duplicate.
    const h = harness({ maxChips: 2 });

    h.arrive(...['a', 'b', 'c'].map(id => h.chip(id, { seed: true })));
    await h.settle(0);
    assert.deepStrictEqual(h.ids(), ['c', 'b'], 'a has been trimmed away');

    h.arrive(h.chip('a', { seed: true }));
    await h.settle(0);
    assert.deepStrictEqual(h.ids(), ['a', 'c']);
});

test('the queue drops the OLDEST pending rolls when it overflows', async () => {
    // A ticker has no scrollback, so a backlog is stale by definition; the newest rolls are the
    // ones worth keeping.
    // The rule, not the production numbers: a queue of 5 exercises it in a fraction of the time
    // that 40 would, and the cadence is irrelevant to what is being asserted.
    const h = harness({ maxChips: 40, maxQueued: 5, slideMs: 1, gapMs: 0 });

    h.arrive(...Array.from({ length: 8 }, (_, i) => h.chip('roll' + i)));
    await h.settle(0);

    assert.ok(h.ticker.pending() <= 5, 'queue is bounded');

    await h.settle(200);
    assert.strictEqual(h.ticker.pending(), 0, 'the queue drained');
    assert.ok(h.ids().includes('roll7'), 'the newest roll survived');
    assert.ok(!h.ids().includes('roll1'), 'the oldest pending rolls were the ones dropped');
});

test('the slide ignores prefers-reduced-motion', async () => {
    // A DELIBERATE OVERRIDE, and the reason this test exists: suppressing the slide left the chip
    // appearing in place and fading, which reads as a flicker rather than as a gentler version of
    // the same idea. Re-adding the guard would look like an accessibility fix and would silently
    // put the ticker back to where it was reported broken, so it is pinned here.
    //
    // Stubbed at the window, which is where the code would have to look. It no longer looks - so
    // this passes trivially today and fails the moment anyone makes it look again.
    const dom = new JSDOM('<!doctype html><body><div id="roll-ticker"></div></body>');
    const track = dom.window.document.getElementById('roll-ticker');
    Object.defineProperty(dom.window.HTMLElement.prototype, 'offsetWidth', { get: () => 100 });
    dom.window.matchMedia = () => ({ matches: true, media: '(prefers-reduced-motion: reduce)' });

    const ticker = createRollTicker(track, { slideMs: SLIDE_MS, gapMs: GAP_MS });

    const starts = [];
    new dom.window.MutationObserver(records => {
        for (const r of records) {
            const m = /translateX\((-?[\d.]+)px\)/.exec(r.oldValue || '');
            if (m && parseFloat(m[1]) !== 0) starts.push(parseFloat(m[1]));
        }
    }).observe(track, { attributes: true, attributeFilter: ['style'], attributeOldValue: true });

    const make = id => { const el = dom.window.document.createElement('span'); el.id = id; return el; };
    [make('a'), make('b')].forEach(el => track.insertAdjacentElement('afterbegin', el));

    await tick(CADENCE * 3);
    assert.strictEqual(track.children.length, 2, 'both rolls landed, still one at a time');
    assert.deepStrictEqual([...new Set(starts)], [-100], 'and each one slid a full chip width');
    ticker.destroy();
});

test('destroy stops the queue draining into a detached track', async () => {
    // An in-app navigation swaps the whole page shell out mid-queue.
    const h = harness();

    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'));
    await h.settle(0);
    h.ticker.destroy();

    const after = h.ids().length;
    await h.settle(CADENCE * 5);
    assert.strictEqual(h.ids().length, after, 'nothing is released after destroy');
});

// ---------------------------------------------------------------------------------------------
// PAUSING WHILE THE TAB IS HIDDEN.
//
// A background tab does not paint, and browsers throttle its timers hard - Chrome to one second,
// then to one MINUTE after five minutes hidden. So a ticker that keeps releasing while hidden
// spends its queue on slides nobody sees, at a cadence nothing is honouring, and the viewer comes
// back to a banner that quietly consumed their backlog. Holding the queue instead means the rolls
// are still there to watch when they return.
//
// The signal is VISIBILITY, not focus. A loot feed is exactly the page people leave open on a
// second monitor while they play, and that window is visible-but-unfocused for hours at a time -
// pausing on blur would freeze the main way this page is used.

test('nothing is released while the tab is hidden', async () => {
    const h = harness();
    await h.setHidden(true);

    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'));
    await h.settle(CADENCE * 5);

    assert.deepStrictEqual(h.ids(), [], 'no chip reaches the track while hidden');
    assert.strictEqual(h.ticker.pending(), 3, 'all three are still queued');
    assert.strictEqual(h.ticker.paused(), true);

    h.ticker.destroy();
});

test('a release already scheduled is cancelled when the tab goes hidden', async () => {
    // The gap between "queued" and "released" is a live timer. Going hidden has to cancel it, or
    // the browser fires it late and the chip slides in unseen anyway.
    const h = harness();

    h.arrive(h.chip('a'), h.chip('b'));
    await h.settle(0);
    assert.deepStrictEqual(h.ids(), ['a'], 'the first is out before we hide');

    await h.setHidden(true);
    await h.settle(CADENCE * 5);

    assert.deepStrictEqual(h.ids(), ['a'], 'the second never followed it');
    assert.strictEqual(h.ticker.pending(), 1);

    h.ticker.destroy();
});

test('the queue resumes when the tab comes back', async () => {
    const h = harness();
    await h.setHidden(true);

    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'));
    await h.settle(CADENCE * 3);
    assert.strictEqual(h.ticker.pending(), 3);

    await h.setHidden(false);
    await h.settle(CADENCE * 4);

    assert.deepStrictEqual(h.ids(), ['c', 'b', 'a'], 'the backlog plays out, oldest first');
    assert.strictEqual(h.ticker.pending(), 0);

    h.ticker.destroy();
});

test('the backlog resumes one at a time, not as a block', async () => {
    // The whole point of the queue. Coming back to twenty rolls must not undo it.
    const h = harness();
    await h.setHidden(true);

    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'), h.chip('d'));
    await h.settle(CADENCE * 2);

    await h.setHidden(false);
    await h.settle(0);
    assert.strictEqual(h.ids().length, 1, 'one chip on resume, not four');

    await h.settle(CADENCE);
    assert.strictEqual(h.ids().length, 2);

    await h.settle(CADENCE * 4);
    assert.strictEqual(h.ids().length, 4, 'and the rest follow at the cadence');

    h.ticker.destroy();
});

test('a long absence keeps the newest rolls, not the oldest', async () => {
    // Hidden for hours, the stream keeps arriving. The queue is capped, so what survives has to be
    // what just happened - coming back to an hour-old backlog labelled live is worse than a gap.
    const h = harness({ maxQueued: 3 });
    await h.setHidden(true);

    h.arrive(h.chip('old-1'), h.chip('old-2'), h.chip('new-1'), h.chip('new-2'), h.chip('new-3'));
    await h.settle(CADENCE);
    assert.strictEqual(h.ticker.pending(), 3, 'the queue is still capped while hidden');

    await h.setHidden(false);
    await h.settle(CADENCE * 5);

    assert.deepStrictEqual(h.ids(), ['new-3', 'new-2', 'new-1']);

    h.ticker.destroy();
});

test('a brief tab switch costs nothing', async () => {
    // Hiding and showing between two releases must not drop a chip or restart the cadence - the
    // common case is an alt-tab, and it should be a no-op.
    const h = harness();

    h.arrive(h.chip('a'), h.chip('b'), h.chip('c'));
    await h.settle(0);

    await h.setHidden(true);
    await h.setHidden(false);
    await h.settle(CADENCE * 4);

    assert.deepStrictEqual(h.ids(), ['c', 'b', 'a'], 'all three still arrive, in order');

    h.ticker.destroy();
});

test('destroy unhooks the visibility listener', async () => {
    // The listener is on the document, which outlives an in-app navigation. Leaving it attached
    // would keep a destroyed ticker's closure alive and let it schedule into a detached track.
    const h = harness();

    h.arrive(h.chip('a'), h.chip('b'));
    await h.settle(0);
    h.ticker.destroy();

    const after = h.ids().length;
    await h.setHidden(true);
    await h.setHidden(false);
    await h.settle(CADENCE * 4);

    assert.strictEqual(h.ids().length, after, 'a destroyed ticker does not wake on visibility');
});

test('the default cadence matches the stylesheet it is pacing against', () => {
    // slideMs and .roll-ticker-track's transition duration are the same number in two files. If
    // they drift, rolls either overlap or leave a hole, and nothing else would notice.
    const fs = require('node:fs');
    const path = require('node:path');
    const { DEFAULTS } = require('../../KlavLor.Web/wwwroot/roll-ticker.js');

    const css = fs.readFileSync(
        path.join(__dirname, '..', '..', 'KlavLor.Web', 'wwwroot', 'app.css'), 'utf8');
    const rule = /\.roll-ticker-track\s*\{[^}]*transition:\s*transform\s+(\d+)ms/.exec(css);

    assert.ok(rule, '.roll-ticker-track must declare a transform transition in ms');
    assert.strictEqual(Number(rule[1]), DEFAULTS.slideMs);
});
