// File System Access API — JS interop for Notes.Core/BrowserFileSystem

const DB_NAME = 'mmn-vault';
const DB_VERSION = 2;
const STORE_NAME = 'handles';
const ENTRY_STORE_NAME = 'entries';

let rootHandle = null;
let vaultName = null;
let vaultId = null;
let vaultMode = null;

/** Try to restore a previously saved vault handle from IndexedDB */
export async function tryRestoreVault(id = null) {
    try {
        const db = await openDB();
        const tx = db.transaction(STORE_NAME, 'readonly');
        const store = tx.objectStore(STORE_NAME);
        const requestedId = id || await readActiveVaultId(store) || 'root';
        const req = store.get(requestedId !== 'root' ? handleKey(requestedId) : 'root');
        const result = await requestToPromise(req);
        if (result?.virtual) {
            rootHandle = null;
            vaultName = result.name || 'Хранилище';
            vaultId = result.vaultId || requestedId;
            vaultMode = 'virtual';
            await ensureVirtualDirectory('');
            try {
                await saveActiveVaultId(vaultId);
            } catch {
                // The vault can still be used for this session.
            }
            return currentVault();
        }

        if (result?.handle && isNativeFileSystemAvailable()) {
            const permission = await result.handle.queryPermission({ mode: 'readwrite' });
            if (permission === 'granted' || (permission === 'prompt' &&
                (await result.handle.requestPermission({ mode: 'readwrite' })) === 'granted')) {
                rootHandle = result.handle;
                vaultName = result.handle.name;
                vaultId = result.vaultId || requestedId || 'root';
                vaultMode = 'native';
                try {
                    await saveActiveVaultId(vaultId);
                } catch {
                    // The vault can still be used for this session.
                }
                return currentVault();
            }
        }
    } catch {
        // Cannot restore
    }
    return null;
}

/** Open a vault directory via picker and persist the handle */
export async function openVault(id = null) {
    if (!isNativeFileSystemAvailable()) {
        return await openVirtualVault(id);
    }

    try {
        rootHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
        vaultName = rootHandle.name;
        vaultId = id || newVaultId();
        vaultMode = 'native';
        await saveHandle(rootHandle, vaultId);
        return currentVault();
    } catch (err) {
        if (err.name === 'AbortError') return null;
        throw err;
    }
}

export async function switchVault(id) {
    return await tryRestoreVault(id);
}

async function saveHandle(handle, id) {
    try {
        const db = await openDB();
        const tx = db.transaction(STORE_NAME, 'readwrite');
        const store = tx.objectStore(STORE_NAME);
        store.put({ id: handleKey(id), vaultId: id, name: handle.name, handle });
        store.put({ id: 'active', vaultId: id });
        await transactionDone(tx);
    } catch {
        // IndexedDB might not be available
    }
}

async function openVirtualVault(id = null) {
    if (!isVirtualFileSystemAvailable()) {
        throw new Error('Хранилище недоступно в этом браузере.');
    }

    const requestedId = id || newVaultId();
    const existing = await readStoredVault(requestedId);
    rootHandle = null;
    vaultName = existing?.name || 'Хранилище';
    vaultId = requestedId;
    vaultMode = 'virtual';
    await saveVirtualVault(vaultId, vaultName);
    await ensureVirtualDirectory('');
    return currentVault();
}

async function saveVirtualVault(id, name) {
    const db = await openDB();
    const tx = db.transaction(STORE_NAME, 'readwrite');
    const store = tx.objectStore(STORE_NAME);
    store.put({ id: handleKey(id), vaultId: id, name, virtual: true });
    store.put({ id: 'active', vaultId: id });
    await transactionDone(tx);
}

async function readStoredVault(id) {
    try {
        const db = await openDB();
        const tx = db.transaction(STORE_NAME, 'readonly');
        const store = tx.objectStore(STORE_NAME);
        return await requestToPromise(store.get(id !== 'root' ? handleKey(id) : 'root'));
    } catch {
        return null;
    }
}

function currentVault() {
    return { Id: vaultId, Name: vaultName, Path: `/browser-vaults/${vaultId}` };
}

function handleKey(id) {
    return `vault:${id}`;
}

function newVaultId() {
    const random = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}${Math.random().toString(16).slice(2)}`;
    return `v_${random.replace(/-/g, '').slice(0, 12)}`;
}

async function readActiveVaultId(store) {
    const req = store.get('active');
    const result = await requestToPromise(req);
    return result?.vaultId || null;
}

async function saveActiveVaultId(id) {
    const db = await openDB();
    const tx = db.transaction(STORE_NAME, 'readwrite');
    const store = tx.objectStore(STORE_NAME);
    store.put({ id: 'active', vaultId: id });
    await transactionDone(tx);
}

function openDB() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            if (!req.result.objectStoreNames.contains(STORE_NAME)) {
                req.result.createObjectStore(STORE_NAME, { keyPath: 'id' });
            }
            if (!req.result.objectStoreNames.contains(ENTRY_STORE_NAME)) {
                req.result.createObjectStore(ENTRY_STORE_NAME, { keyPath: 'id' });
            }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

async function resolvePath(rootHandle, relativePath) {
    if (!relativePath) return rootHandle;
    const parts = relativePath.split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part);
    }
    return current;
}

/** Check if a relative path exists as directory */
export async function directoryExists(relativePath) {
    if (isVirtualVault()) return await virtualDirectoryExists(relativePath);
    if (!rootHandle) return false;
    if (!relativePath) return true;
    try {
        await resolvePath(rootHandle, relativePath);
        return true;
    } catch {
        return false;
    }
}

/** Check if a relative path exists as file */
export async function fileExists(relativePath) {
    if (isVirtualVault()) return await virtualFileExists(relativePath);
    if (!rootHandle) return false;
    if (!relativePath) return false;
    const parts = relativePath.split('/').filter(p => p);
    if (parts.length === 0) return false;
    const fileName = parts.pop();
    try {
        const dir = parts.length > 0 ? await resolvePath(rootHandle, parts.join('/')) : rootHandle;
        await dir.getFileHandle(fileName);
        return true;
    } catch {
        return false;
    }
}

/** Create a directory relative to root */
export async function createDirectory(relativePath) {
    if (isVirtualVault()) {
        await ensureVirtualDirectory(relativePath);
        return;
    }
    if (!rootHandle || !relativePath) return;
    const parts = relativePath.split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part, { create: true });
    }
}

/** Read entire file content as text */
export async function readAllText(relativePath) {
    if (isVirtualVault()) return await readVirtualText(relativePath);
    if (!rootHandle) throw new Error('No vault opened');
    if (!relativePath) throw new Error('Invalid path');
    const parts = relativePath.split('/').filter(p => p);
    if (parts.length === 0) throw new Error('Invalid path');
    const fileName = parts.pop();
    const dir = parts.length > 0 ? await resolvePath(rootHandle, parts.join('/')) : rootHandle;
    const fileHandle = await dir.getFileHandle(fileName);
    const file = await fileHandle.getFile();
    return await file.text();
}

/** Write text content to file */
export async function writeAllText(relativePath, contents) {
    if (isVirtualVault()) {
        await writeVirtualText(relativePath, contents);
        return;
    }
    if (!rootHandle) throw new Error('No vault opened');
    if (!relativePath) throw new Error('Invalid path');
    const parts = relativePath.split('/').filter(p => p);
    if (parts.length === 0) throw new Error('Invalid path');
    const fileName = parts.pop();
    let current = rootHandle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part, { create: true });
    }
    const fileHandle = await current.getFileHandle(fileName, { create: true });
    const writable = await fileHandle.createWritable();
    await writable.write(contents);
    await writable.close();
}

/** Delete a file relative to root. Missing files are treated as already gone. */
export async function deleteFile(relativePath) {
    if (isVirtualVault()) {
        await deleteVirtualFile(relativePath);
        return;
    }
    if (!rootHandle) throw new Error('No vault opened');
    if (!relativePath) throw new Error('Invalid path');
    const parts = relativePath.split('/').filter(p => p);
    if (parts.length === 0) throw new Error('Invalid path');
    const fileName = parts.pop();
    const dir = parts.length > 0 ? await resolvePath(rootHandle, parts.join('/')) : rootHandle;
    try {
        await dir.removeEntry(fileName);
    } catch (err) {
        if (err.name !== 'NotFoundError') throw err;
    }
}

/** Enumerate files matching a glob pattern in a directory tree */
export async function enumerateFiles(dirPath, pattern, recurse) {
    if (isVirtualVault()) return await enumerateVirtualFiles(dirPath, pattern, recurse);
    if (!rootHandle) return [];
    const results = [];
    const parts = (dirPath || '').split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        try {
            current = await current.getDirectoryHandle(part);
        } catch {
            return [];
        }
    }

    // Convert *.md glob to regex
    const regex = globToRegex(pattern);
    await walk(current, dirPath, regex, !!recurse, results);
    return results;
}

async function walk(dirHandle, basePath, regex, recurse, results) {
    for await (const [name, handle] of dirHandle.entries()) {
        const fullPath = basePath ? `${basePath}/${name}` : name;
        if (handle.kind === 'file' && regex.test(name)) {
            results.push(fullPath);
        } else if (handle.kind === 'directory' && recurse) {
            await walk(handle, fullPath, regex, recurse, results);
        }
    }
}

/** Check if API is available in this browser */
export function isAvailable() {
    return isNativeFileSystemAvailable() || isVirtualFileSystemAvailable();
}

function isNativeFileSystemAvailable() {
    return typeof window !== 'undefined' && typeof window.showDirectoryPicker === 'function';
}

function isVirtualFileSystemAvailable() {
    return typeof indexedDB !== 'undefined';
}

function isVirtualVault() {
    return vaultMode === 'virtual' && vaultId;
}

function normalizePath(relativePath) {
    const parts = String(relativePath || '')
        .replace(/\\/g, '/')
        .split('/')
        .filter(Boolean);

    if (parts.some(part => part === '..')) {
        throw new Error('Invalid path');
    }

    return parts.join('/');
}

function parentPath(path) {
    const index = path.lastIndexOf('/');
    return index < 0 ? '' : path.slice(0, index);
}

function fileName(path) {
    const index = path.lastIndexOf('/');
    return index < 0 ? path : path.slice(index + 1);
}

function virtualEntryKey(path) {
    return `${vaultId}:${path || '.'}`;
}

async function getVirtualEntry(path) {
    const db = await openDB();
    const tx = db.transaction(ENTRY_STORE_NAME, 'readonly');
    const store = tx.objectStore(ENTRY_STORE_NAME);
    return await requestToPromise(store.get(virtualEntryKey(path)));
}

async function putVirtualEntry(entry) {
    const db = await openDB();
    const tx = db.transaction(ENTRY_STORE_NAME, 'readwrite');
    const store = tx.objectStore(ENTRY_STORE_NAME);
    store.put({ ...entry, id: virtualEntryKey(entry.path), vaultId });
    await transactionDone(tx);
}

async function listVirtualEntries() {
    const db = await openDB();
    const tx = db.transaction(ENTRY_STORE_NAME, 'readonly');
    const store = tx.objectStore(ENTRY_STORE_NAME);
    const entries = await requestToPromise(store.getAll());
    return (entries || []).filter(entry => entry.vaultId === vaultId);
}

async function ensureVirtualDirectory(relativePath) {
    if (!isVirtualVault()) return;
    const path = normalizePath(relativePath);
    let current = '';
    await putVirtualEntry({ path: current, kind: 'directory' });
    for (const part of path.split('/').filter(Boolean)) {
        current = current ? `${current}/${part}` : part;
        await putVirtualEntry({ path: current, kind: 'directory' });
    }
}

async function virtualDirectoryExists(relativePath) {
    if (!isVirtualVault()) return false;
    const path = normalizePath(relativePath);
    if (!path) return true;

    const entry = await getVirtualEntry(path);
    if (entry?.kind === 'directory') return true;

    const prefix = `${path}/`;
    const entries = await listVirtualEntries();
    return entries.some(candidate => candidate.path.startsWith(prefix));
}

async function virtualFileExists(relativePath) {
    if (!isVirtualVault()) return false;
    const path = normalizePath(relativePath);
    if (!path) return false;

    const entry = await getVirtualEntry(path);
    return entry?.kind === 'file';
}

async function readVirtualText(relativePath) {
    if (!isVirtualVault()) throw new Error('No vault opened');
    const path = normalizePath(relativePath);
    if (!path) throw new Error('Invalid path');

    const entry = await getVirtualEntry(path);
    if (entry?.kind !== 'file') throw new Error('File not found');
    return entry.contents || '';
}

async function writeVirtualText(relativePath, contents) {
    if (!isVirtualVault()) throw new Error('No vault opened');
    const path = normalizePath(relativePath);
    if (!path) throw new Error('Invalid path');

    await ensureVirtualDirectory(parentPath(path));
    await putVirtualEntry({ path, kind: 'file', contents: contents || '' });
}

async function deleteVirtualFile(relativePath) {
    if (!isVirtualVault()) throw new Error('No vault opened');
    const path = normalizePath(relativePath);
    if (!path) throw new Error('Invalid path');

    const db = await openDB();
    const tx = db.transaction(ENTRY_STORE_NAME, 'readwrite');
    const store = tx.objectStore(ENTRY_STORE_NAME);
    store.delete(virtualEntryKey(path));
    await transactionDone(tx);
}

async function enumerateVirtualFiles(dirPath, pattern, recurse) {
    if (!isVirtualVault()) return [];
    const dir = normalizePath(dirPath);
    if (!await virtualDirectoryExists(dir)) return [];

    const prefix = dir ? `${dir}/` : '';
    const regex = globToRegex(pattern);
    const entries = await listVirtualEntries();
    return entries
        .filter(entry => entry.kind === 'file')
        .map(entry => entry.path)
        .filter(path => path.startsWith(prefix))
        .filter(path => {
            const rest = path.slice(prefix.length);
            return !!rest && (!!recurse || !rest.includes('/')) && regex.test(fileName(path));
        })
        .sort((a, b) => a.localeCompare(b));
}

function globToRegex(pattern) {
    const escaped = String(pattern || '*').replace(/[.+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`^${escaped.replace(/\*/g, '.*')}$`);
}

function requestToPromise(req) {
    return new Promise((resolve, reject) => {
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

function transactionDone(tx) {
    return new Promise((resolve, reject) => {
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error);
    });
}
