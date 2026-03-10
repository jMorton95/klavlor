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
        activeEntity = "home";
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
    preview.innerHTML =
        (iconUrl ? '<img src="' + iconUrl + '" alt="" class="w-6 h-6 object-contain" onerror="this.style.display=\'none\'" />' : '') +
        '<span class="flex-1 truncate text-slate-800 dark:text-slate-200">' + name.replace(/</g, '&lt;') + '</span>' +
        '<button type="button" onclick="clearOsrsSelection(this)" class="text-slate-400 hover:text-slate-600">' +
        '<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/></svg>' +
        '</button>';
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

// Include antiforgery token in all HTMX requests
document.body.addEventListener('htmx:configRequest', function(e) {
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (token) {
        e.detail.headers['RequestVerificationToken'] = token;
    }
});

document.body.addEventListener('htmx:afterSettle', updateSidebarActive);
window.addEventListener('popstate', updateSidebarActive);

updateSidebarActive();
