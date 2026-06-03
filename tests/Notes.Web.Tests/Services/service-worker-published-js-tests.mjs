import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const serviceWorkerPath = new URL('../../../src/Notes.Web/wwwroot/service-worker.published.js', import.meta.url);

const source = await readFile(serviceWorkerPath, 'utf8');

assert.match(source, /skipWaiting\(\)/, 'published service worker should activate updates immediately');
assert.match(source, /clients\.claim\(\)/, 'published service worker should claim controlled clients immediately');
assert.match(source, /registration\.unregister\(\)/, 'published service worker should unregister after clearing stale caches');
assert.match(source, /caches\.delete\(/, 'published service worker should delete stale offline caches');
assert.doesNotMatch(source, /cache\.addAll\(/, 'published service worker should not pre-cache app bundles');
assert.doesNotMatch(source, /cache\.match\(/, 'published service worker should not serve stale cached app bundles');

console.log('service-worker-published-js-tests: 6 passed');
