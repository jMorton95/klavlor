(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.RollTicker = factory();
}(typeof self !== 'undefined' ? self : this, function () {
    'use strict';
    var DEFAULTS = {
        slideMs: 550,
        gapMs: 200,

        maxQueued: 40
    };
    function createRollTicker(track, options) {
        var opts = Object.assign({}, DEFAULTS, options || {});
        var view = (track.ownerDocument && track.ownerDocument.defaultView) || null;
        var Observer = (view && view.MutationObserver)
            || (typeof MutationObserver !== 'undefined' ? MutationObserver : null);
        var computedStyle = (view && view.getComputedStyle)
            || (typeof getComputedStyle !== 'undefined' ? getComputedStyle : null);
        var Matrix = (view && view.DOMMatrixReadOnly)
            || (typeof DOMMatrixReadOnly !== 'undefined' ? DOMMatrixReadOnly : null);
        var queue = [];
        var ours = new WeakSet();
        var present = new Set();
        var timer = 0;
        var lastReleaseAt = 0;
        function now() { return Date.now(); }

        function currentShift() {
            if (computedStyle) {
                var t = computedStyle(track).transform;
                if (t && t !== 'none') {
                    if (Matrix) { try { return new Matrix(t).m41; } catch (e) {  } }
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
            if (!advance) return;
            var from = currentShift() - advance;
            track.style.transition = 'none';
            track.style.transform = 'translateX(' + from + 'px)';
            if (computedStyle) computedStyle(track).transform;
            track.style.transition = '';
            track.style.transform = 'translateX(0px)';
        }
        function schedule() {
            if (timer || !queue.length) return;
            var wait = Math.max(0, lastReleaseAt + opts.slideMs + opts.gapMs - now());
            timer = setTimeout(function () { timer = 0; release(); }, wait);
        }
        function release() {
            if (!track.isConnected) { queue.length = 0; return; }
            var chip = queue.shift();
            if (!chip) return;

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
                    if (node.id && present.has(node.id)) { node.remove(); continue; }
                    if (node.id) present.add(node.id);

                    if (node.hasAttribute('data-seed')) continue;
                    arrived.push(node);
                }
            }

            if (!arrived.length) { trim(); return; }
            for (var k = 0; k < arrived.length; k++) { arrived[k].remove(); queue.push(arrived[k]); }
            if (queue.length > opts.maxQueued) queue.splice(0, queue.length - opts.maxQueued).forEach(forget);
            trim();
            schedule();
        }
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
    if (typeof module !== 'object' && typeof document !== 'undefined') {
        initRollTicker();
        document.body.addEventListener('htmx:afterSettle', function (evt) {
            if (evt.detail && evt.detail.target && evt.detail.target.id === 'hx-page-container') {
                initRollTicker();
            }
        });
    }
    return { createRollTicker: createRollTicker, initRollTicker: initRollTicker, DEFAULTS: DEFAULTS };
}));
