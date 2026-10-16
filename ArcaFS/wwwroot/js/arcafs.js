// ArcaFS-only JS interop. Theme, modal and rename helpers live in the NetForge Bootstrap
// package's forge.js (window.netForge); only the file browser's own needs are kept here.
window.arcafs = {
    _keyHandlers: new Map(),

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
    }
};
