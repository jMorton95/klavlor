// --- Dark Mode Toggle ---
function toggleDarkMode() {
    var isDark = document.documentElement.classList.toggle('dark');
    localStorage.setItem('theme', isDark ? 'dark' : 'light');
}

document.addEventListener('click', function(e) {
    if (e.target.closest('[data-toggle="dark-mode"]')) {
        toggleDarkMode();
    }
});

const toggleSidebar = () => {
    const sidebar = document.getElementById('mobile-sidebar');
    const backdrop = document.getElementById('sidebar-backdrop');
    sidebar.classList.toggle('-translate-x-full');
    backdrop.classList.toggle('hidden');
}

document.addEventListener('click', (e) => {
    const toggleBtn = e.target.closest('[data-toggle="sidebar"]');
    if (toggleBtn) {
        toggleSidebar();
    }
})

function updateSidebarActive() {
    const path = window.location.pathname;
    const sidebarLinks = document.querySelectorAll('#sidebar a');
    let activeEntity = null;

    if (path === "/" || path === "") {
        activeEntity = "templates";
    } else {
        const segments = path.split('/').filter(Boolean);
        for (const link of sidebarLinks) {
            const section = link.dataset.section;
            if (segments.includes(section)) {
                activeEntity = section;
                break;
            }
        }
    }

    sidebarLinks.forEach(link => {
        if (link.dataset.section === activeEntity) {
            link.setAttribute('aria-current', 'page');
        } else {
            link.removeAttribute('aria-current');
        }
    });
}

window.removeToast = function(button) {
    const toast = button.closest('[role=alert]');
    toast.style.animation = 'fade-out 0.5s ease forwards';
    setTimeout(() => toast.remove(), 300);
}

window.fadeOutToast = function(progressBar) {
    const toast = progressBar.closest('[role=alert]');
    toast.style.animation = 'fade-out 0.5s ease forwards';
    setTimeout(() => toast.remove(), 300);
}

// OSRS Wiki search - delegated click handler for search results
document.addEventListener('click', function(e) {
    var btn = e.target.closest('.osrs-search-result');
    if (!btn) return;
    e.preventDefault();
    selectOsrsItem(btn, btn.dataset.itemName, btn.dataset.itemIcon);
});

// OSRS Wiki search - select an item from the dropdown
window.selectOsrsItem = function(el, name, iconUrl) {
    var modal = el.closest('#modal-element');
    if (!modal) return;

    modal.querySelector('[name=Label]').value = name;
    modal.querySelector('[name=IconUrl]').value = iconUrl || '';

    // Hide search input, show selected item preview
    var searchInput = modal.querySelector('#osrs-search-input');
    if (searchInput) searchInput.classList.add('hidden');

    var existing = modal.querySelector('#selected-item');
    if (existing) existing.remove();

    var preview = document.createElement('div');
    preview.id = 'selected-item';
    preview.className = 'flex items-center gap-2 px-3 py-2 text-sm border border-slate-300 dark:border-slate-600 rounded-lg bg-slate-50 dark:bg-slate-800';

    if (iconUrl) {
        var img = document.createElement('img');
        img.src = iconUrl;
        img.alt = '';
        img.className = 'w-6 h-6 object-contain';
        img.onerror = function() { this.style.display = 'none'; };
        preview.appendChild(img);
    }

    var span = document.createElement('span');
    span.className = 'flex-1 truncate text-slate-800 dark:text-slate-200';
    span.textContent = name;
    preview.appendChild(span);

    var clearBtn = document.createElement('button');
    clearBtn.type = 'button';
    clearBtn.className = 'text-slate-400 hover:text-slate-600';
    clearBtn.addEventListener('click', function() { clearOsrsSelection(this); });
    clearBtn.innerHTML = '<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/></svg>';
    preview.appendChild(clearBtn);

    searchInput.parentElement.insertBefore(preview, searchInput);

    // Clear results
    var results = modal.querySelector('#osrs-search-results');
    if (results) results.innerHTML = '';
};

// Clear the OSRS item selection and show search input again
window.clearOsrsSelection = function(el) {
    var modal = el.closest('#modal-element');
    if (!modal) return;

    modal.querySelector('[name=Label]').value = '';
    modal.querySelector('[name=IconUrl]').value = '';

    var selected = modal.querySelector('#selected-item');
    if (selected) selected.remove();

    var searchInput = modal.querySelector('#osrs-search-input');
    if (searchInput) {
        searchInput.classList.remove('hidden');
        searchInput.value = '';
        searchInput.focus();
    }
};

// --- Session loot modal: toggle between grouped loot and individual rolls ---
window.toggleSessionView = function(btn) {
    var modal = btn.closest('#modal-element');
    if (!modal) return;
    var grouped = modal.querySelector('#session-grouped');
    var rolls = modal.querySelector('#session-rolls');
    if (!grouped || !rolls) return;
    var label = btn.querySelector('[data-session-toggle-label]');
    var showRolls = rolls.classList.contains('hidden');
    rolls.classList.toggle('hidden', !showRolls);
    grouped.classList.toggle('hidden', showRolls);
    if (label) label.textContent = showRolls ? 'View grouped loot' : 'View individual rolls';
};

// Get the center of the builder canvas viewport for placing new nodes
window.getCanvasViewportCenter = function() {
    var canvas = document.getElementById('builder-canvas');
    if (!canvas) return { x: 400, y: 300 };
    return {
        x: Math.round(canvas.scrollLeft + canvas.clientWidth / 2 - 90),
        y: Math.round(canvas.scrollTop + canvas.clientHeight / 2 - 40)
    };
};

// --- Completion Popover ---
window.openCompletionPopover = function(nodeId) {
    // Close any other open popovers first
    document.querySelectorAll('[id^="completion-popover-"]').forEach(function(p) {
        p.classList.add('hidden');
    });
    var popover = document.getElementById('completion-popover-' + nodeId);
    if (popover) {
        popover.classList.remove('hidden');
        var input = popover.querySelector('input[name="note"]');
        if (input) input.focus();
    }
};

window.closeCompletionPopover = function(nodeId) {
    var popover = document.getElementById('completion-popover-' + nodeId);
    if (popover) popover.classList.add('hidden');
};

// Close completion popover on outside click
document.addEventListener('click', function(e) {
    if (e.target.closest('[id^="completion-popover-"]')) return;
    if (e.target.closest('[onclick^="openCompletionPopover"]')) return;
    document.querySelectorAll('[id^="completion-popover-"]').forEach(function(p) {
        p.classList.add('hidden');
    });
});

// --- Completion History Panel toggle ---
// State lives on <body> (preserved across panel OOB swaps + canvas re-renders); persisted in localStorage.
window.HISTORY_PANEL_KEY = 'klavlor.viewer.historyOpen';

window.syncHistoryToggleButton = function() {
    var btn = document.getElementById('history-toggle-btn');
    if (!btn) return;
    var open = document.body.classList.contains('history-panel-open');
    btn.setAttribute('aria-pressed', open ? 'true' : 'false');
    var active = ['bg-amber-500', 'text-white', 'border-amber-500', 'dark:bg-amber-600', 'dark:border-amber-600', 'dark:text-white'];
    var inactive = ['text-amber-600', 'dark:text-amber-400', 'border-amber-300', 'dark:border-amber-600'];
    if (open) {
        active.forEach(function(c) { btn.classList.add(c); });
        inactive.forEach(function(c) { btn.classList.remove(c); });
    } else {
        active.forEach(function(c) { btn.classList.remove(c); });
        inactive.forEach(function(c) { btn.classList.add(c); });
    }
};

window.toggleHistoryPanel = function() {
    var open = !document.body.classList.contains('history-panel-open');
    document.body.classList.toggle('history-panel-open', open);
    try { localStorage.setItem(window.HISTORY_PANEL_KEY, open ? '1' : '0'); } catch {}
    window.syncHistoryToggleButton();
};

window.initHistoryPanel = function() {
    if (!document.getElementById('completion-history-panel')) {
        document.body.classList.remove('history-panel-open');
        return;
    }
    var saved = '0';
    try { saved = localStorage.getItem(window.HISTORY_PANEL_KEY) || '0'; } catch {}
    document.body.classList.toggle('history-panel-open', saved === '1');
    window.syncHistoryToggleButton();
};

// --- Loot Feed Filter ---
(() => {
    const ALL_TIERS = ['standard', 'uncommon', 'rare', 'epic', 'legendary'];
    const STORAGE_KEY = 'lootFeedFilter';
    // Each feed page renders #feed-grid-container with a data-grid-url attribute
    // pointing at its scope-specific grid endpoint (main vs leagues). Falls back
    // to the main grid URL if the attribute is missing (defensive).
    function getGridApi() {
        const container = document.getElementById('feed-grid-container');
        return container?.dataset.gridUrl || '/api/loot/feed/grid';
    }
    // Both main and leagues grid endpoints end in /grid; this lets us inject the
    // tiers query param onto either via htmx:configRequest.
    const GRID_PATH_SUFFIX = '/grid';

    function getActiveTiers() {
        try {
            const saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
            if (Array.isArray(saved) && saved.length > 0) return saved;
        } catch {}
        return ALL_TIERS;
    }

    // Only syncs the checkboxes to the saved filter. It deliberately does NOT re-fetch the grid:
    // #feed-grid-container carries hx-trigger="load" and fetches itself, and the configRequest
    // hook below appends the saved tiers to that request — so the first grid the user sees is
    // already filtered. Re-fetching here would fire a second identical request and re-open every
    // SSE stream.
    function initFeedFilter() {
        const checkboxes = document.querySelectorAll('.feed-filter-checkbox');
        if (checkboxes.length === 0) return;

        const active = getActiveTiers();
        for (const cb of checkboxes) {
            cb.checked = active.includes(cb.value);
        }
    }

    window.toggleFeedFilter = function() {
        const panel = document.getElementById('feed-filter-panel');
        if (panel) panel.classList.toggle('hidden');
    };

    window.saveFeedFilter = function() {
        const checked = Array.from(document.querySelectorAll('.feed-filter-checkbox:checked')).map(cb => cb.value);
        localStorage.setItem(STORAGE_KEY, JSON.stringify(checked));
        const panel = document.getElementById('feed-filter-panel');
        if (panel) panel.classList.add('hidden');
        const noTiers = document.getElementById('feed-no-tiers');
        if (noTiers) noTiers.classList.add('hidden');

        if (checked.length === 0) {
            const container = document.getElementById('feed-grid-container');
            if (container) container.innerHTML = '';
            if (noTiers) noTiers.classList.remove('hidden');
            return;
        }

        htmx.ajax('GET', getGridApi() + '?tiers=' + checked.join(','), {
            target: '#feed-grid-container',
            swap: 'innerHTML'
        });
    };

    // Inject tiers param into HTMX requests for any feed grid API (main or leagues).
    document.body.addEventListener('htmx:configRequest', function(evt) {
        if (evt.detail.path && evt.detail.path.startsWith('/api/loot/feed') && evt.detail.path.endsWith(GRID_PATH_SUFFIX)) {
            const tiers = getActiveTiers();
            evt.detail.parameters['tiers'] = tiers.join(',');
        }
    });

    // Close filter panel when clicking outside
    document.addEventListener('click', function(e) {
        const panel = document.getElementById('feed-filter-panel');
        if (!panel) return;
        if (!panel.classList.contains('hidden') && !e.target.closest('#feed-filter-panel') && !e.target.closest('[onclick="toggleFeedFilter()"]')) {
            panel.classList.add('hidden');
        }
    });

    // Initialize on page load
    initFeedFilter();

    // Re-initialize only when the page-level container is swapped (HTMX navigation)
    document.body.addEventListener('htmx:afterSettle', function(evt) {
        if (evt.detail.target && evt.detail.target.id === 'hx-page-container') {
            initFeedFilter();
        }
    });
})();

// --- Page-transition loader ---
// Shows #page-loader during full-page HTMX navigations (requests that swap #hx-page-container
// AND push a URL — i.e. real navigations, not search-as-you-type or sub-panel loads). A short
// delay keeps instant loads from flashing the spinner; a failsafe prevents a stuck overlay.
(() => {
    let showTimer = null, failsafe = null;
    const SHOW_DELAY = 120;     // ms before the spinner appears
    const MAX_VISIBLE = 20000;  // ms hard cap so a dropped request can't leave it spinning

    const loader = () => document.getElementById('page-loader');
    const isPageContainer = (e) => e.detail && e.detail.target && e.detail.target.id === 'hx-page-container';
    const isNavigation = (e) => isPageContainer(e) && !!(e.detail.elt && e.detail.elt.closest && e.detail.elt.closest('[hx-push-url]'));

    function show() {
        loader()?.classList.remove('hidden');
        clearTimeout(failsafe);
        failsafe = setTimeout(hide, MAX_VISIBLE);
    }
    function hide() {
        clearTimeout(showTimer);
        clearTimeout(failsafe);
        loader()?.classList.add('hidden');
    }

    document.body.addEventListener('htmx:beforeRequest', (e) => {
        if (!isNavigation(e)) return;
        clearTimeout(showTimer);
        showTimer = setTimeout(show, SHOW_DELAY);
    });

    // afterRequest fires on both success and HTTP errors; the network-failure events cover the rest.
    ['htmx:afterRequest', 'htmx:responseError', 'htmx:sendError', 'htmx:timeout'].forEach((ev) =>
        document.body.addEventListener(ev, (e) => { if (isPageContainer(e)) hide(); })
    );

    // History navigation (browser Back/Forward). beforeHistorySave fires before HTMX snapshots the
    // DOM — hiding first keeps the cached snapshot from capturing a visible spinner; historyRestore
    // fires when that snapshot is put back. Neither event carries the page-container detail, so hide
    // unconditionally rather than gating on isPageContainer.
    ['htmx:beforeHistorySave', 'htmx:historyRestore'].forEach((ev) =>
        document.body.addEventListener(ev, hide)
    );
})();

// --- Component-swap loader ---
// Shows a small spinner over the swap target (or the clicked element) whenever a GET HTMX
// component fetch — tab switch, sub-panel, "show more", modal open — runs longer than ~100ms.
// Full-page navigations (target #hx-page-container) are handled by the page loader above.
(() => {
    const DELAY = 100;          // ms before the spinner appears
    const MAX_VISIBLE = 30000;  // ms hard cap so nothing can linger
    const pending = new Map();  // triggering element -> { timer, failsafe, overlay }

    // The portion of an element's box that's actually on screen, so the spinner centres in the
    // visible area even when the target is taller than the viewport.
    function visibleRect(el) {
        if (!el || !el.getBoundingClientRect) return null;
        const r = el.getBoundingClientRect();
        const top = Math.max(r.top, 0);
        const bottom = Math.min(r.bottom, window.innerHeight);
        const left = Math.max(r.left, 0);
        const right = Math.min(r.right, window.innerWidth);
        if (right - left < 4 || bottom - top < 4) return null;
        return { left, top, width: right - left, height: bottom - top };
    }

    function remove(key) {
        const p = pending.get(key);
        if (!p) return;
        clearTimeout(p.timer);
        clearTimeout(p.failsafe);
        if (p.overlay) p.overlay.remove();
        pending.delete(key);
    }

    document.body.addEventListener('htmx:beforeRequest', (e) => {
        const elt = e.detail.elt;
        const target = e.detail.target;
        if (!elt || pending.has(elt)) return;

        // GET-only — component fetches. Mutations/position-drag POSTs don't get a section loader.
        const verb = ((e.detail.requestConfig && e.detail.requestConfig.verb) || '').toLowerCase();
        const isGet = verb ? verb === 'get' : !!(elt.closest && elt.closest('[hx-get]'));
        if (!isGet) return;

        // Full-page navigations have their own overlay (#page-loader).
        if (target && target.id === 'hx-page-container') return;

        const entry = { timer: null, failsafe: null, overlay: null };
        entry.timer = setTimeout(() => {
            // Prefer the swap target; fall back to the clicked element (e.g. a modal open whose
            // target is the empty, zero-size #hx-modal-container).
            let rect = (target && target.id !== 'hx-modal-container') ? visibleRect(target) : null;
            if (!rect) rect = visibleRect(elt);
            if (!rect) { pending.delete(elt); return; }
            const o = document.createElement('div');
            o.className = 'hx-section-loader';
            o.style.left = rect.left + 'px';
            o.style.top = rect.top + 'px';
            o.style.width = rect.width + 'px';
            o.style.height = rect.height + 'px';
            document.body.appendChild(o);
            entry.overlay = o;
            entry.failsafe = setTimeout(() => remove(elt), MAX_VISIBLE);
        }, DELAY);
        pending.set(elt, entry);
    });

    // Clear on any terminal event. A swap with hx-swap="outerHTML" can detach the triggering
    // element (e.g. tab buttons that live inside their own swap target), so its afterRequest
    // won't bubble here — afterSwap/afterSettle still fire on the attached new content, and we
    // also sweep any entry whose trigger is no longer in the document.
    function sweep(e) {
        const elt = e.detail && e.detail.elt;
        for (const key of [...pending.keys()]) {
            if (key === elt || !key.isConnected) remove(key);
        }
    }
    ['htmx:afterRequest', 'htmx:afterSwap', 'htmx:afterSettle', 'htmx:responseError', 'htmx:sendError', 'htmx:timeout']
        .forEach((ev) => document.body.addEventListener(ev, sweep));

    // History navigation can't be matched by trigger element — the saved snapshot would otherwise
    // keep a body-appended overlay around forever. Clear every pending overlay on save/restore.
    function clearAll() { for (const key of [...pending.keys()]) remove(key); }
    ['htmx:beforeHistorySave', 'htmx:historyRestore'].forEach((ev) =>
        document.body.addEventListener(ev, clearAll)
    );
})();

// Include antiforgery token in all HTMX requests
document.body.addEventListener('htmx:configRequest', function(e) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (token) {
        e.detail.headers['RequestVerificationToken'] = token;
    }
});

// --- Last Viewed Template Tracking ---
function trackLastViewedTemplate() {
    var match = window.location.pathname.match(/^\/templates\/(\d+)(\/builder)?$/);
    if (match) {
        localStorage.setItem('lastViewedTemplate', match[1]);
    }
}

document.body.addEventListener('htmx:afterSettle', function() {
    updateSidebarActive();
    trackLastViewedTemplate();

    // Auto-close mobile sidebar after navigation
    var sidebar = document.getElementById('mobile-sidebar');
    var backdrop = document.getElementById('sidebar-backdrop');
    if (sidebar && !sidebar.classList.contains('-translate-x-full')) {
        sidebar.classList.add('-translate-x-full');
        backdrop.classList.add('hidden');
    }
});
window.addEventListener('popstate', updateSidebarActive);

updateSidebarActive();

// --- Activity heatmap hover tooltip ---
// Document-level delegation so it keeps working after HTMX swaps the heatmap in/out.
(function () {
    let tip = null;

    function ensureTip() {
        if (!tip) {
            tip = document.createElement('div');
            tip.className = 'heatmap-tooltip';
            tip.setAttribute('role', 'tooltip');
            document.body.appendChild(tip);
        }
        return tip;
    }

    function position(clientX, clientY) {
        if (!tip) return;
        const pad = 14;
        const r = tip.getBoundingClientRect();
        let x = clientX + pad;
        let y = clientY + pad;
        if (x + r.width > window.innerWidth - 8) x = clientX - r.width - pad;
        if (y + r.height > window.innerHeight - 8) y = clientY - r.height - pad;
        tip.style.left = Math.max(8, x) + 'px';
        tip.style.top = Math.max(8, y) + 'px';
    }

    document.addEventListener('mouseover', function (e) {
        const cell = e.target.closest && e.target.closest('.heatmap-cell');
        if (!cell) return;
        const t = ensureTip();
        const d = cell.dataset;
        if (d.empty === 'true') {
            t.innerHTML = '<div class="hm-tt-date">' + d.date + '</div>' +
                '<div class="hm-tt-empty">No activity</div>';
        } else {
            t.innerHTML = '<div class="hm-tt-date">' + d.date + '</div>' +
                '<div><span class="hm-tt-val hm-tt-gp">' + d.gp + '</span> gp</div>' +
                '<div><span class="hm-tt-val">' + d.kills + '</span> kills</div>' +
                '<div><span class="hm-tt-val hm-tt-clog">' + d.clogs + '</span> new clogs</div>';
        }
        t.classList.add('is-visible');
        position(e.clientX, e.clientY);
    });

    document.addEventListener('mousemove', function (e) {
        if (tip && tip.classList.contains('is-visible')) position(e.clientX, e.clientY);
    });

    document.addEventListener('mouseout', function (e) {
        const cell = e.target.closest && e.target.closest('.heatmap-cell');
        if (!cell || !tip) return;
        // Don't flicker when sliding straight onto an adjacent cell.
        const to = e.relatedTarget;
        if (to && to.closest && to.closest('.heatmap-cell')) return;
        tip.classList.remove('is-visible');
    });
})();
trackLastViewedTemplate();
