// Uploads go over plain HTTP rather than through the Blazor circuit. InputFile would stream every
// byte over SignalR, which caps out well below the multi-gigabyte files a file browser is expected
// to handle and blocks the circuit while it does. XHR also gives us real progress events.
window.arcafsUploader = {
    _dropZones: new Map(),
    _active: new Map(),
    _nextId: 1,

    /**
     * Registers drag-and-drop on an element. Reports dropped files back to .NET, which decides
     * about overwrites before any upload starts.
     */
    registerDropZone: function (key, element, dotNetRef) {
        if (!(element instanceof Element) || !dotNetRef) {
            return;
        }

        this.unregisterDropZone(key);

        var depth = 0;

        var onDragEnter = function (e) {
            e.preventDefault();

            // dragenter/dragleave fire for descendants too, so a counter is needed or the
            // highlight flickers as the pointer crosses child elements.
            depth++;
            if (depth === 1) {
                element.classList.add('arca-drop-active');
            }
        };

        var onDragOver = function (e) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
        };

        var onDragLeave = function (e) {
            e.preventDefault();
            depth = Math.max(0, depth - 1);
            if (depth === 0) {
                element.classList.remove('arca-drop-active');
            }
        };

        var onDrop = async function (e) {
            e.preventDefault();
            depth = 0;
            element.classList.remove('arca-drop-active');

            var entries = await window.arcafsUploader._readDataTransfer(e.dataTransfer);
            if (entries.length === 0) {
                return;
            }

            window.arcafsUploader._pending = entries;

            dotNetRef.invokeMethodAsync('OnFilesDropped', entries.map(function (entry) {
                return { name: entry.file.name, size: entry.file.size, relativePath: entry.path };
            }));
        };

        element.addEventListener('dragenter', onDragEnter);
        element.addEventListener('dragover', onDragOver);
        element.addEventListener('dragleave', onDragLeave);
        element.addEventListener('drop', onDrop);

        this._dropZones.set(key, { element, onDragEnter, onDragOver, onDragLeave, onDrop });
    },

    unregisterDropZone: function (key) {
        var zone = this._dropZones.get(key);
        if (!zone) {
            return;
        }

        zone.element.removeEventListener('dragenter', zone.onDragEnter);
        zone.element.removeEventListener('dragover', zone.onDragOver);
        zone.element.removeEventListener('dragleave', zone.onDragLeave);
        zone.element.removeEventListener('drop', zone.onDrop);
        this._dropZones.delete(key);
    },

    /** Opens the OS file picker and reports the chosen files, mirroring a drop. */
    pickFiles: function (dotNetRef, directories) {
        var input = document.createElement('input');
        input.type = 'file';
        input.multiple = true;

        if (directories) {
            input.webkitdirectory = true;
        }

        input.addEventListener('change', function () {
            var entries = Array.prototype.map.call(input.files, function (file) {
                return { file: file, path: file.webkitRelativePath || file.name };
            });

            window.arcafsUploader._pending = entries;

            dotNetRef.invokeMethodAsync('OnFilesDropped', entries.map(function (entry) {
                return { name: entry.file.name, size: entry.file.size, relativePath: entry.path };
            }));
        });

        input.click();
    },

    /**
     * Uploads the files reported by the last drop or pick.
     * @param {number} sourceId Target source.
     * @param {string} path Target directory within the source.
     * @param {boolean} overwrite Whether the server may replace existing files.
     * @param {object} dotNetRef Receives OnUploadProgress / OnUploadComplete.
     */
    uploadPending: async function (sourceId, path, overwrite, dotNetRef) {
        var entries = this._pending || [];
        this._pending = [];

        for (var i = 0; i < entries.length; i++) {
            var entry = entries[i];
            var uploadId = this._nextId++;

            try {
                await this._uploadOne(uploadId, sourceId, path, entry, overwrite, dotNetRef);
                await dotNetRef.invokeMethodAsync('OnUploadComplete', uploadId, entry.file.name, true, null);
            } catch (error) {
                await dotNetRef.invokeMethodAsync(
                    'OnUploadComplete', uploadId, entry.file.name, false, String(error && error.message || error));
            }
        }

        await dotNetRef.invokeMethodAsync('OnUploadBatchComplete');
    },

    cancelUpload: function (uploadId) {
        var request = this._active.get(uploadId);
        if (request) {
            request.abort();
            this._active.delete(uploadId);
        }
    },

    _uploadOne: function (uploadId, sourceId, path, entry, overwrite, dotNetRef) {
        var form = new FormData();
        form.append('file', entry.file, entry.file.name);
        form.append('sourceId', String(sourceId));
        form.append('path', path);
        form.append('relativePath', entry.path || entry.file.name);
        form.append('overwrite', overwrite ? 'true' : 'false');

        return this._send('/api/files/upload', form, uploadId, entry, dotNetRef);
    },

    _send: function (url, form, uploadId, entry, dotNetRef) {
        var self = this;

        return new Promise(function (resolve, reject) {
            var request = new XMLHttpRequest();
            request.open('POST', url, true);

            // Throttled, because a fast local upload fires progress events far more often than
            // the circuit can usefully re-render.
            var lastReport = 0;

            request.upload.onprogress = function (e) {
                if (!e.lengthComputable) {
                    return;
                }

                var now = Date.now();
                if (now - lastReport < 150 && e.loaded < e.total) {
                    return;
                }
                lastReport = now;

                dotNetRef.invokeMethodAsync(
                    'OnUploadProgress', uploadId, entry.file.name, e.loaded, e.total);
            };

            request.onload = function () {
                self._active.delete(uploadId);

                if (request.status >= 200 && request.status < 300) {
                    resolve();
                } else {
                    var message = request.responseText;
                    try {
                        message = JSON.parse(request.responseText).detail || message;
                    } catch (e) { /* not a problem-details response */ }

                    reject(new Error(message || ('HTTP ' + request.status)));
                }
            };

            request.onerror = function () {
                self._active.delete(uploadId);
                reject(new Error('The connection was lost.'));
            };

            request.onabort = function () {
                self._active.delete(uploadId);
                reject(new Error('Cancelled.'));
            };

            self._active.set(uploadId, request);
            request.send(form);
        });
    },

    /** Same pipeline as uploadPending, but through a share token instead of a source id. */
    uploadPendingToShare: async function (token, path, password, dotNetRef) {
        var entries = this._pending || [];
        this._pending = [];

        for (var i = 0; i < entries.length; i++) {
            var entry = entries[i];
            var uploadId = this._nextId++;

            var form = new FormData();
            form.append('file', entry.file, entry.file.name);
            form.append('path', path);

            if (password) {
                form.append('password', password);
            }

            try {
                await this._send(
                    '/api/public/' + encodeURIComponent(token) + '/upload',
                    form, uploadId, entry, dotNetRef);

                await dotNetRef.invokeMethodAsync(
                    'OnUploadComplete', uploadId, entry.file.name, true, null);
            } catch (error) {
                await dotNetRef.invokeMethodAsync(
                    'OnUploadComplete', uploadId, entry.file.name, false,
                    String(error && error.message || error));
            }
        }

        await dotNetRef.invokeMethodAsync('OnUploadBatchComplete');
    },

    /**
     * Flattens a drop into files with their relative paths, walking directories where the
     * browser exposes them so dropping a folder preserves its structure.
     */
    _readDataTransfer: async function (dataTransfer) {
        var items = dataTransfer.items;
        var results = [];

        if (!items || typeof items[0]?.webkitGetAsEntry !== 'function') {
            return Array.prototype.map.call(dataTransfer.files, function (file) {
                return { file: file, path: file.name };
            });
        }

        var roots = [];
        for (var i = 0; i < items.length; i++) {
            var entry = items[i].webkitGetAsEntry();
            if (entry) {
                roots.push(entry);
            }
        }

        for (var j = 0; j < roots.length; j++) {
            await this._walkEntry(roots[j], '', results);
        }

        return results;
    },

    _walkEntry: function (entry, prefix, results) {
        var self = this;

        return new Promise(function (resolve) {
            if (entry.isFile) {
                entry.file(function (file) {
                    results.push({ file: file, path: prefix + file.name });
                    resolve();
                }, resolve);

                return;
            }

            if (!entry.isDirectory) {
                resolve();
                return;
            }

            var reader = entry.createReader();
            var children = [];

            // readEntries returns at most 100 entries per call, so it has to be drained.
            var readBatch = function () {
                reader.readEntries(async function (batch) {
                    if (batch.length === 0) {
                        for (var i = 0; i < children.length; i++) {
                            await self._walkEntry(children[i], prefix + entry.name + '/', results);
                        }
                        resolve();
                        return;
                    }

                    children = children.concat(Array.prototype.slice.call(batch));
                    readBatch();
                }, resolve);
            };

            readBatch();
        });
    }
};
