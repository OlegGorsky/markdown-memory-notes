// File System Access API — JS interop for Notes.Core/BrowserFileSystem

const DB_NAME = 'mmn-vault';
const DB_VERSION = 1;
const STORE_NAME = 'handles';

let rootHandle = null;
let vaultName = null;

/** Try to restore a previously saved vault handle from IndexedDB */
export async function tryRestoreVault() {
    try {
        const db = await openDB();
        const tx = db.transaction(STORE_NAME, 'readonly');
        const store = tx.objectStore(STORE_NAME);
        const req = store.get('root');
        const result = await new Promise((resolve, reject) => {
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
        if (result && result.handle) {
            const permission = await result.handle.queryPermission({ mode: 'readwrite' });
            if (permission === 'granted' || (permission === 'prompt' &&
                (await result.handle.requestPermission({ mode: 'readwrite' })) === 'granted')) {
                rootHandle = result.handle;
                vaultName = result.handle.name;
                return vaultName;
            }
        }
    } catch {
        // Cannot restore
    }
    return null;
}

/** Open a vault directory via picker and persist the handle */
export async function openVault() {
    try {
        rootHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
        vaultName = rootHandle.name;
        await saveHandle(rootHandle);
        return vaultName;
    } catch (err) {
        if (err.name === 'AbortError') return null;
        throw err;
    }
}

async function saveHandle(handle) {
    try {
        const db = await openDB();
        const tx = db.transaction(STORE_NAME, 'readwrite');
        const store = tx.objectStore(STORE_NAME);
        store.put({ id: 'root', handle });
        await new Promise((resolve, reject) => {
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    } catch {
        // IndexedDB might not be available
    }
}

function openDB() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            req.result.createObjectStore(STORE_NAME, { keyPath: 'id' });
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

function resolvePath(rootHandle, relativePath) {
    if (!relativePath) return rootHandle;
    const parts = relativePath.split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        current = current.getDirectoryHandle(part);
    }
    return current;
}

/** Check if a relative path exists as directory */
export async function directoryExists(relativePath) {
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
    if (!rootHandle || !relativePath) return;
    const parts = relativePath.split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part, { create: true });
    }
}

/** Read entire file content as text */
export async function readAllText(relativePath) {
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

/** Enumerate files matching a glob pattern in a directory tree */
export async function enumerateFiles(dirPath, pattern, recurse) {
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
    const regex = new RegExp('^' + pattern.replace(/\./g, '\\.').replace(/\*/g, '.*') + '$');
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
    return typeof window !== 'undefined' && 'showDirectoryPicker' in window;
}
