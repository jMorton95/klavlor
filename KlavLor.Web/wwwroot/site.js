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

// --- Loot Feed Filter ---
(() => {
    const ALL_TIERS = ['standard', 'uncommon', 'rare', 'epic', 'legendary'];
    const STORAGE_KEY = 'lootFeedFilter';
    const GRID_API = '/api/loot/feed/grid';

    function getActiveTiers() {
        try {
            const saved = JSON.parse(localStorage.getItem(STORAGE_KEY));
            if (Array.isArray(saved) && saved.length > 0) return saved;
        } catch {}
        return ALL_TIERS;
    }

    function initFeedFilter() {
        const checkboxes = document.querySelectorAll('.feed-filter-checkbox');
        if (checkboxes.length === 0) return;

        const active = getActiveTiers();
        for (const cb of checkboxes) {
            cb.checked = active.includes(cb.value);
        }

        // If user has a non-default filter, re-fetch with their filter applied
        if (JSON.stringify([...active].sort()) !== JSON.stringify([...ALL_TIERS].sort())) {
            htmx.ajax('GET', GRID_API + '?tiers=' + active.join(','), {
                target: '#feed-grid-container',
                swap: 'innerHTML'
            });
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

        htmx.ajax('GET', GRID_API + '?tiers=' + checked.join(','), {
            target: '#feed-grid-container',
            swap: 'innerHTML'
        });
    };

    // Inject tiers param into HTMX requests for the feed grid API
    document.body.addEventListener('htmx:configRequest', function(evt) {
        if (evt.detail.path && evt.detail.path.includes('/api/loot/feed/grid')) {
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
trackLastViewedTemplate();
