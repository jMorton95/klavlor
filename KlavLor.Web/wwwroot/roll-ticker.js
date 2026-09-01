/*
The live roll ticker on the loot feed: a broadcast banner that carries one roll at a time.

Its own file rather than a block in site.js because the queue below is the kind of thing that
breaks silently - a batching regression still shows every chip, just all at once - and site.js is a
grab-bag of browser-only glue that cannot be loaded outside a browser. Everything here is pure DOM,
so tests/js/roll-ticker.test.js drives it under jsdom with no browser at all.

THE SLIDE. A chip prepended at its natural width shoves every existing chip right by that width.
We cancel the shove with a translate on the TRACK before the browser paints, then release it, so
the row slides rather than jumps. One composited transform per roll, on one element, whatever is on
screen. The mask lives on the frame around the track, not on the track, or the right-edge fade
would travel with the chips.

THE QUEUE. htmx swaps every SSE frame in the moment it arrives, so a sync that lands five kills at
once inserts five chips in one task - and one slide of five chip widths carries the lot in
together, which reads as a jolt rather than as news arriving. Arrivals are detached on sight and
released one at a time. Nothing is lost: order is preserved and the row ends up exactly where htmx
would have left it, just paced.

THE DEDUPE. An EventSource reconnects on any blip - a sleep, a proxy timeout, a deploy - and the
server answers by replaying its whole ring. Without keying on LootRollEntry.DomId the banner shows
those rolls twice over.

A MutationObserver rather than an htmx event, because the timing is what makes all three work: an
observer callback is a microtask and is guaranteed to run before the next paint, so a chip can be
detached, deduped, or shifted without ever having been drawn. htmx:afterSettle is a setTimeout away
- at least one frame later - by which point the batch has already been painted in place.
*/
(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.RollTicker = factory();
}(typeof self !== 'undefined' ? self : this, function () {
    'use strict';

    var DEFAULTS = {
        // slideMs MUST match .roll-ticker-track's transition duration in app.css: it is what paces
        // the queue behind it. Change one without the other and rolls overlap or leave a gap.
        slideMs: 550,
        gapMs: 200,
        // A ticker has no scrollback, so a backlog is stale by definition. Matches the server ring
        // (ILootRollFeed.BacklogSize): past it the OLDEST pending rolls go, not the newest.
        maxQueued: 40
    };

    function prefersReducedMotion() {
        return typeof window !== 'undefined' && typeof window.matchMedia === 'function'
            ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
            : false;
    }

    function createRollTicker(track, options) {
        var opts = Object.assign({}, DEFAULTS, options || {});
        var reduced = opts.reducedMotion || prefersReducedMotion;

        // Take MutationObserver and getComputedStyle from the TRACK'S OWN window rather than the
        // bare globals. In a browser these are the same object; outside one - the jsdom tests -
        // there are no such globals at all, and reaching for them is a ReferenceError.
        var view = (track.ownerDocument && track.ownerDocument.defaultView) || null;
        var Observer = (view && view.MutationObserver)
            || (typeof MutationObserver !== 'undefined' ? MutationObserver : null);
        var computedStyle = (view && view.getComputedStyle)
            || (typeof getComputedStyle !== 'undefined' ? getComputedStyle : null);
        var Matrix = (view && view.DOMMatrixReadOnly)
            || (typeof DOMMatrixReadOnly !== 'undefined' ? DOMMatrixReadOnly : null);

        var queue = [];
        // Chips we put back ourselves. A flag would not do: the observer runs as a microtask, so it
        // fires after the synchronous block that would have cleared one.
        var ours = new WeakSet();
        // Every roll on screen or waiting in the queue, by DomId. Bounded by construction: an id
        // goes in when a chip is accepted and comes out when that chip is trimmed or dropped.
        var present = new Set();
        var timer = 0;
        var lastReleaseAt = 0;

        function now() { return Date.now(); }

        // The COMPUTED transform, not the inline one. Inline is always translateX(0px) the instant
        // slide() returns - it is the target, not where the row actually is - so reading it would
        // make a roll released mid-slide start from zero and jerk the row backwards instead of
        // adding to the travel still in progress. Only the computed value tracks the transition.
        function currentShift() {
            if (computedStyle) {
                var t = computedStyle(track).transform;
                if (t && t !== 'none') {
                    if (Matrix) { try { return new Matrix(t).m41; } catch (e) { /* parse below */ } }
                    var mx = /matrix\((?:\s*[-\d.e]+\s*,){4}\s*(-?[\d.e]+)/.exec(t);
                    if (mx) return parseFloat(mx[1]);
                    var tx = /translateX\((-?[\d.]+)px\)/.exec(t);
                    if (tx) return parseFloat(tx[1]);
                }
            }
            var inline = /translateX\((-?[\d.]+)px\)/.exec(track.style.transform || '');
            return inline ? parseFloat(inline[1]) : 0;
        }

        function forget(chip) { if (chip && chip.id) present.delete(chip.id); }

        function trim() {
            var max = parseInt(track.dataset.maxChips, 10) || opts.maxQueued;
            while (track.children.length > max) forget(track.removeChild(track.lastElementChild));
        }

        function slide(advance) {
            if (!advance || reduced()) return;
            // Read the live offset rather than a remembered one, so a roll released while the
            // previous one is still travelling adds to where the track has got to.
            var from = currentShift() - advance;
            track.style.transition = 'none';
            track.style.transform = 'translateX(' + from + 'px)';
            if (computedStyle) computedStyle(track).transform; // flush: the release must transition from here
            track.style.transition = '';
            track.style.transform = 'translateX(0px)';
        }

        // The cadence is held against the CLOCK, not against the queue being non-empty. Draining
        // the queue and letting the next arrival straight through is the bug this replaced: rolls
        // land a millisecond or two apart, so by the time the second arrived the queue was already
        // empty, nothing gated it, and the batch stacked into one slide anyway.
        function schedule() {
            if (timer || !queue.length) return;
            var wait = Math.max(0, lastReleaseAt + opts.slideMs + opts.gapMs - now());
            timer = setTimeout(function () { timer = 0; release(); }, wait);
        }

        function release() {
            if (!track.isConnected) { queue.length = 0; return; }

            var chip = queue.shift();
            if (!chip) return;

            // Insert and shift in ONE synchronous block, so the un-shifted row is never painted.
            lastReleaseAt = now();
            ours.add(chip);
            track.insertBefore(chip, track.firstChild);
            slide(chip.offsetWidth);
            trim();

            schedule();
        }

        function onMutations(records) {
            var arrived = [];
            for (var i = 0; i < records.length; i++) {
                var added = records[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    var node = added[j];
                    if (node.nodeType !== 1) continue;
                    if (ours.has(node)) continue;
                    // A roll we already hold. Drop it before it is ever painted.
                    if (node.id && present.has(node.id)) { node.remove(); continue; }
                    if (node.id) present.add(node.id);
                    // Backfill is history, not news: it arrives in one burst on connect and belongs
                    // on screen immediately, in place.
                    if (node.hasAttribute('data-seed')) continue;
                    arrived.push(node);
                }
            }

            if (!arrived.length) { trim(); return; }

            // Records are chronological and each SSE frame carries one chip, so this is arrival
            // order - which is the order they should be read in, and prepending them in that order
            // leaves the row exactly as htmx would have.
            for (var k = 0; k < arrived.length; k++) { arrived[k].remove(); queue.push(arrived[k]); }
            if (queue.length > opts.maxQueued) queue.splice(0, queue.length - opts.maxQueued).forEach(forget);
            trim();

            schedule();
        }

        // An in-app navigation binds this on htmx:afterSettle, by which point htmx has already
        // processed the new nodes and opened the EventSource. Connecting is a network round-trip so
        // frames realistically arrive later than that, but adopting what is already there costs
        // nothing and removes the question entirely.
        for (var c = 0; c < track.children.length; c++) {
            if (track.children[c].id) present.add(track.children[c].id);
        }

        var observer = new Observer(onMutations);
        observer.observe(track, { childList: true });

        return {
            destroy: function () {
                observer.disconnect();
                clearTimeout(timer);
                timer = 0;
                queue.length = 0;
            },
            // Tests only. The queue is the thing worth asserting on and it is not in the DOM.
            pending: function () { return queue.length; }
        };
    }

    function initRollTicker(doc, options) {
        var d = doc || (typeof document !== 'undefined' ? document : null);
        if (!d) return null;
        var track = d.getElementById('roll-ticker');
        if (!track || track.dataset.tickerBound) return null;
        track.dataset.tickerBound = '1';
        return createRollTicker(track, options);
    }

    // Self-initialising in a browser, so nothing else has to know this file exists or load in any
    // particular order. Skipped under CommonJS, which is how the tests get an uninitialised module.
    if (typeof module !== 'object' && typeof document !== 'undefined') {
        initRollTicker();
        // The banner lives in the page shell, so an in-app navigation swaps in a fresh, unbound track.
        document.body.addEventListener('htmx:afterSettle', function (evt) {
            if (evt.detail && evt.detail.target && evt.detail.target.id === 'hx-page-container') {
                initRollTicker();
            }
        });
    }

    return { createRollTicker: createRollTicker, initRollTicker: initRollTicker, DEFAULTS: DEFAULTS };
}));
