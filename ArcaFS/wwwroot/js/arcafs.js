// Small, flat surface for Blazor JS interop. Each property on `window.arcafs` is
// single-purpose so the C# call sites read naturally.
window.arcafs = {
    _observers: new Map(),
    _themeModalDismissRegistered: new Set(),
    _keyHandlers: new Map(),

    /**
     * Live-swaps the active Bootswatch stylesheet without a page reload.
     * @param {string} href Path under wwwroot to the theme's bootstrap.min.css.
     * @param {string} [dataBsTheme] "dark" or "light". Must match the theme, or Bootstrap's
     *   [data-bs-theme=dark] variable overrides won't apply and text goes unreadable.
     */
    setThemeHref: function (href, dataBsTheme) {
        var link = document.getElementById('theme-stylesheet');
        if (link) {
            link.setAttribute('href', href);
        }
        if (dataBsTheme === 'dark' || dataBsTheme === 'light') {
            document.documentElement.setAttribute('data-bs-theme', dataBsTheme);
        }
    },

    /** Raw value from localStorage; may be empty or invalid, so validate in .NET. */
    getStoredTheme: function () {
        try {
            return localStorage.getItem('arcafs.theme') || '';
        } catch (e) {
            return '';
        }
    },

    setStoredTheme: function (theme) {
        try {
            localStorage.setItem('arcafs.theme', theme);
        } catch (e) {
            /* private mode or blocked storage */
        }
    },

    getStoredViewMode: function () {
        try {
            return localStorage.getItem('arcafs.viewMode') || '';
        } catch (e) {
            return '';
        }
    },

    setStoredViewMode: function (mode) {
        try {
            localStorage.setItem('arcafs.viewMode', mode);
        } catch (e) {
            /* private mode or blocked storage */
        }
    },

    /** Wires Bootstrap's hidden.bs.modal so Blazor can revert an uncommitted theme preview. */
    registerThemeModalDismiss: function (modalId, dotNetRef) {
        if (window.arcafs._themeModalDismissRegistered.has(modalId)) {
            return;
        }
        var el = document.getElementById(modalId);
        if (!el) {
            return;
        }
        el.addEventListener('hidden.bs.modal', function () {
            dotNetRef.invokeMethodAsync('OnThemeModalDismissed');
        });
        window.arcafs._themeModalDismissRegistered.add(modalId);
    },

    showModal: function (modalId) {
        var el = document.getElementById(modalId);
        if (!el || typeof bootstrap === 'undefined') {
            return;
        }
        bootstrap.Modal.getOrCreateInstance(el).show();
    },

    hideModal: function (modalId) {
        var el = document.getElementById(modalId);
        if (!el || typeof bootstrap === 'undefined') {
            return;
        }
        var instance = bootstrap.Modal.getInstance(el);
        if (instance) {
            instance.hide();
        }
    },

    /** Native confirm, so message escaping stays on the .NET side. */
    confirmDialog: function (message) {
        return window.confirm(message);
    },

    focusElement: function (element) {
        if (element && typeof element.focus === 'function') {
            element.focus();
        }
    },

    selectFileNameStem: function (element) {
        if (!element) {
            return;
        }
        element.focus();

        // Select everything up to the extension so renaming "photo.jpg" doesn't make the user
        // retype ".jpg" or risk losing it.
        var value = element.value || '';
        var dot = value.lastIndexOf('.');
        element.setSelectionRange(0, dot > 0 ? dot : value.length);
    },

    /**
     * Keyboard shortcuts for the file browser, registered while the page is mounted.
     * Ignores keystrokes aimed at inputs so typing a filename doesn't trigger Delete.
     */
    registerFileKeys: function (key, dotNetRef) {
        this.unregisterFileKeys(key);

        var handler = function (e) {
            var target = e.target;
            if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA'
                || target.isContentEditable)) {
                return;
            }

            var combo = (e.ctrlKey || e.metaKey ? 'ctrl+' : '') + (e.shiftKey ? 'shift+' : '') + e.key;
            var handled = ['ctrl+a', 'Delete', 'F2', 'ctrl+c', 'ctrl+x', 'ctrl+v', 'Escape', '/'];

            if (handled.indexOf(combo) >= 0) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnShortcut', combo);
            }
        };

        document.addEventListener('keydown', handler);
        this._keyHandlers.set(key, handler);
    },

    unregisterFileKeys: function (key) {
        var handler = this._keyHandlers.get(key);
        if (handler) {
            document.removeEventListener('keydown', handler);
            this._keyHandlers.delete(key);
        }
    },

    observeInfiniteScroll: function (key, element, dotNetRef, callbackMethodName) {
        if (!key || !(element instanceof Element) || !dotNetRef || !callbackMethodName) {
            return;
        }

        this.disconnectInfiniteScroll(key);

        var observer = new IntersectionObserver(function (entries) {
            for (var i = 0; i < entries.length; i++) {
                if (entries[i].isIntersecting) {
                    dotNetRef.invokeMethodAsync(callbackMethodName);
                    break;
                }
            }
        }, { root: null, threshold: 0.1 });

        observer.observe(element);
        this._observers.set(key, observer);
    },

    disconnectInfiniteScroll: function (key) {
        var observer = this._observers.get(key);
        if (observer) {
            observer.disconnect();
            this._observers.delete(key);
        }
    },

    /** Triggers a browser download without navigating the SPA away from the current page. */
    downloadUrl: function (url) {
        var a = document.createElement('a');
        a.href = url;
        a.download = '';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    },

    copyToClipboard: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (e) {
            return false;
        }
    },

    scrollIntoView: function (element) {
        if (element && typeof element.scrollIntoView === 'function') {
            element.scrollIntoView({ block: 'nearest' });
        }
    }
};
