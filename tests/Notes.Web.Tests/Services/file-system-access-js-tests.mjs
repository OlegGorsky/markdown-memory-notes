import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const modulePath = new URL('../../../src/Notes.Web/wwwroot/js/file-system-access.js', import.meta.url);

async function loadFileSystemAccess() {
  const source = await readFile(modulePath, 'utf8');
  return import(`data:text/javascript,${encodeURIComponent(source)}#${Date.now()}-${Math.random()}`);
}

function installBrowserHarness() {
  globalThis.window = {};
  globalThis.indexedDB = new FakeIndexedDB();
}

async function testVirtualVaultOpensWithoutDirectoryPicker() {
  installBrowserHarness();
  const fs = await loadFileSystemAccess();

  const vault = await fs.openVault('vault_a');

  assert.deepEqual(vault, {
    Id: 'vault_a',
    Name: 'Хранилище',
    Path: '/browser-vaults/vault_a'
  });
  assert.equal(fs.isAvailable(), true);

  await fs.createDirectory('notes/projects');
  await fs.writeAllText('notes/projects/a.md', 'hello');
  await fs.writeAllText('notes/root.md', 'root');

  assert.equal(await fs.directoryExists('notes'), true);
  assert.equal(await fs.directoryExists('notes/projects'), true);
  assert.equal(await fs.fileExists('notes/projects/a.md'), true);
  assert.equal(await fs.readAllText('notes/projects/a.md'), 'hello');
  assert.deepEqual(await fs.enumerateFiles('notes', '*.md', false), ['notes/root.md']);
  assert.deepEqual(await fs.enumerateFiles('notes', '*.md', true), [
    'notes/projects/a.md',
    'notes/root.md'
  ]);

  await fs.deleteFile('notes/projects/a.md');
  assert.equal(await fs.fileExists('notes/projects/a.md'), false);
}

async function testVirtualVaultRestoresFromIndexedDb() {
  installBrowserHarness();
  const fs = await loadFileSystemAccess();

  await fs.openVault('vault_restore');
  await fs.writeAllText('notes/restored.md', 'persisted');

  const restoredFs = await loadFileSystemAccess();
  const restored = await restoredFs.tryRestoreVault();

  assert.equal(restored.Id, 'vault_restore');
  assert.equal(restored.Name, 'Хранилище');
  assert.equal(await restoredFs.readAllText('notes/restored.md'), 'persisted');
}

async function testNativePickerIsUsedWhenAvailable() {
  installBrowserHarness();
  let pickerCalled = false;
  globalThis.window.showDirectoryPicker = async () => {
    pickerCalled = true;
    return { name: 'Native notes' };
  };
  const fs = await loadFileSystemAccess();

  const vault = await fs.openVault('native_a');

  assert.equal(pickerCalled, true);
  assert.deepEqual(vault, {
    Id: 'native_a',
    Name: 'Native notes',
    Path: '/browser-vaults/native_a'
  });
}

async function testCreateVirtualVaultBypassesNativePicker() {
  installBrowserHarness();
  let pickerCalled = false;
  globalThis.window.showDirectoryPicker = async () => {
    pickerCalled = true;
    return { name: 'Native notes' };
  };
  const fs = await loadFileSystemAccess();

  const vault = await fs.createVirtualVault('paired_a');

  assert.equal(pickerCalled, false);
  assert.deepEqual(vault, {
    Id: 'paired_a',
    Name: 'Хранилище',
    Path: '/browser-vaults/paired_a'
  });

  await fs.writeAllText('notes/synced.md', 'from pc');
  assert.equal(await fs.readAllText('notes/synced.md'), 'from pc');
}

async function testAbortFromNativePickerReturnsNull() {
  installBrowserHarness();
  globalThis.window.showDirectoryPicker = async () => {
    throw new DOMException('cancelled', 'AbortError');
  };
  const fs = await loadFileSystemAccess();

  assert.equal(await fs.openVault('cancelled'), null);
}

class FakeIndexedDB {
  constructor() {
    this.databases = new Map();
  }

  open(name, version) {
    const request = {};
    queueMicrotask(() => {
      let record = this.databases.get(name);
      let upgrade = false;
      if (!record) {
        record = { version: 0, stores: new Map() };
        this.databases.set(name, record);
        upgrade = true;
      }

      if (version > record.version) {
        record.version = version;
        upgrade = true;
      }

      request.result = new FakeDatabase(record);
      if (upgrade) {
        request.onupgradeneeded?.();
      }

      request.onsuccess?.();
    });
    return request;
  }
}

class FakeDatabase {
  constructor(record) {
    this.record = record;
    this.objectStoreNames = {
      contains: name => this.record.stores.has(name)
    };
  }

  createObjectStore(name, options) {
    const store = { keyPath: options.keyPath, records: new Map() };
    this.record.stores.set(name, store);
    return new FakeObjectStore(store, null);
  }

  transaction(storeName) {
    return new FakeTransaction(this.record, storeName);
  }
}

class FakeTransaction {
  constructor(record, storeName) {
    this.record = record;
    this.storeName = storeName;
    this.error = null;
    this.completeQueued = false;
  }

  objectStore(name) {
    assert.equal(name, this.storeName);
    const store = this.record.stores.get(name);
    assert.ok(store, `store ${name} should exist`);
    return new FakeObjectStore(store, this);
  }

  completeSoon() {
    if (this.completeQueued) return;
    this.completeQueued = true;
    queueMicrotask(() => this.oncomplete?.());
  }
}

class FakeObjectStore {
  constructor(store, transaction) {
    this.store = store;
    this.transaction = transaction;
  }

  get(key) {
    return this.request(() => this.store.records.get(key));
  }

  getAll() {
    return this.request(() => Array.from(this.store.records.values()));
  }

  put(value) {
    this.store.records.set(value[this.store.keyPath], value);
    this.transaction?.completeSoon();
    return this.request(() => value);
  }

  delete(key) {
    this.store.records.delete(key);
    this.transaction?.completeSoon();
    return this.request(() => undefined);
  }

  request(action) {
    const request = {};
    queueMicrotask(() => {
      try {
        request.result = action();
        request.onsuccess?.();
      } catch (error) {
        request.error = error;
        request.onerror?.();
      }
    });
    return request;
  }
}

const tests = [
  testVirtualVaultOpensWithoutDirectoryPicker,
  testVirtualVaultRestoresFromIndexedDb,
  testNativePickerIsUsedWhenAvailable,
  testCreateVirtualVaultBypassesNativePicker,
  testAbortFromNativePickerReturnsNull
];

for (const test of tests) {
  await test();
}

console.log(`file-system-access-js-tests: ${tests.length} passed`);
