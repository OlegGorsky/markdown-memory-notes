// File System Access API — JS interop for Notes.Core/BrowserFileSystem

let rootHandle = null;
const handles = {};

/** Open a vault directory via picker */
export async function openVault() {
    try {
        rootHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
        return rootHandle.name;
    } catch (err) {
        if (err.name === 'AbortError') return null;
        throw err;
    }
}

/** Check if a relative path exists as directory */
export async function directoryExists(relativePath) {
    if (!rootHandle) return false;
    const parts = relativePath.split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        try {
            current = await current.getDirectoryHandle(part);
        } catch {
            return false;
        }
    }
    return true;
}

/** Check if a relative path exists as file */
export async function fileExists(relativePath) {
    if (!rootHandle) return false;
    const parts = relativePath.split('/').filter(p => p);
    if (parts.length === 0) return false;
    const fileName = parts.pop();
    let current = rootHandle;
    for (const part of parts) {
        try {
            current = await current.getDirectoryHandle(part);
        } catch {
            return false;
        }
    }
    try {
        await current.getFileHandle(fileName);
        return true;
    } catch {
        return false;
    }
}

/** Create a directory relative to root */
export async function createDirectory(relativePath) {
    if (!rootHandle) return;
    const parts = relativePath.split('/').filter(p => p);
    let current = rootHandle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part, { create: true });
    }
}

/** Read entire file content as text */
export async function readAllText(relativePath) {
    if (!rootHandle) throw new Error('No vault opened');
    const parts = relativePath.split('/').filter(p => p);
    const fileName = parts.pop();
    let current = rootHandle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part);
    }
    const fileHandle = await current.getFileHandle(fileName);
    const file = await fileHandle.getFile();
    return await file.text();
}

/** Write text content to file */
export async function writeAllText(relativePath, contents) {
    if (!rootHandle) throw new Error('No vault opened');
    const parts = relativePath.split('/').filter(p => p);
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
    const parts = dirPath.split('/').filter(p => p);
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
