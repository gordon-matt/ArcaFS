// ArcaFS-only JS interop. Theme switching lives in the NetForge shell; this file is the file browser.
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

    getStoredShowTree: function () {
        try {
            return localStorage.getItem('arcafs.showTree') || '';
        } catch (e) {
            return '';
        }
    },

    setStoredShowTree: function (value) {
        try {
            localStorage.setItem('arcafs.showTree', value);
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

            var key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
            var combo = (e.ctrlKey || e.metaKey ? 'ctrl+' : '') + (e.shiftKey ? 'shift+' : '') + key;
            var handled = ['ctrl+a', 'ctrl+shift+i', 'ctrl+z', 'ctrl+y', 'ctrl+shift+z', 'Delete', 'F2', 'ctrl+c', 'ctrl+x', 'ctrl+v', 'Escape', '/'];

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

    _itemDrags: new Map(),

    /**
     * Dragging an existing item onto a folder. The custom type lets the upload drop zone
     * ignore it, and the path is read in the drop handler so it does not depend on a
     * round-trip back from the server.
     */
    registerItemDrag: function (key, element, dotNetRef) {
        this.unregisterItemDrag(key);
        if (!(element instanceof Element) || !dotNetRef) {
            return;
        }

        var current = null;

        var hasItems = function (event) {
            var types = event.dataTransfer && event.dataTransfer.types;
            if (!types) {
                return false;
            }

            return Array.prototype.indexOf.call(types, 'application/x-arcafs-items') >= 0;
        };

        var onDragStart = function (event) {
            var row = event.target.closest('[data-arca-path]');
            if (!row || row.getAttribute('data-arca-draggable') !== 'true') {
                return;
            }

            var path = row.getAttribute('data-arca-path') || '';
            event.dataTransfer.setData('application/x-arcafs-items', path);
            event.dataTransfer.effectAllowed = 'copyMove';
            dotNetRef.invokeMethodAsync('OnItemDragStart', path);
        };

        var onDragOver = function (event) {
            if (!hasItems(event)) {
                return;
            }

            var row = event.target.closest('[data-arca-drop]');
            if (!row) {
                return;
            }

            event.preventDefault();
            event.dataTransfer.dropEffect = (event.ctrlKey || event.metaKey) ? 'copy' : 'move';
            if (current !== row) {
                if (current) {
                    current.classList.remove('drop-target');
                }

                row.classList.add('drop-target');
                current = row;
            }
        };

        var clearTarget = function () {
            if (current) {
                current.classList.remove('drop-target');
                current = null;
            }
        };

        var onDrop = function (event) {
            if (!hasItems(event)) {
                return;
            }

            event.preventDefault();
            var row = event.target.closest('[data-arca-drop]');
            clearTarget();
            if (!row) {
                return;
            }

            event.stopPropagation();
            var source = event.dataTransfer.getData('application/x-arcafs-items');
            var target = row.getAttribute('data-arca-drop') || '';
            dotNetRef.invokeMethodAsync('OnItemsDropped', source, target, !!(event.ctrlKey || event.metaKey));
        };

        element.addEventListener('dragstart', onDragStart);
        element.addEventListener('dragover', onDragOver);
        element.addEventListener('dragend', clearTarget);
        element.addEventListener('drop', onDrop);
        this._itemDrags.set(key, { element, onDragStart, onDragOver, onDrop, clearTarget });
    },

    unregisterItemDrag: function (key) {
        var drag = this._itemDrags.get(key);
        if (!drag) {
            return;
        }

        drag.element.removeEventListener('dragstart', drag.onDragStart);
        drag.element.removeEventListener('dragover', drag.onDragOver);
        drag.element.removeEventListener('dragend', drag.clearTarget);
        drag.element.removeEventListener('drop', drag.onDrop);
        this._itemDrags.delete(key);
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
