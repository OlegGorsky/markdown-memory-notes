/* Manifest version: iDx16PNG */
const cacheNamePrefix = 'offline-cache-';

self.addEventListener('install', event => {
    console.info('Service worker: install update');
    self.skipWaiting();
    event.waitUntil(deleteOfflineCaches());
});

self.addEventListener('activate', event => {
    console.info('Service worker: activate update');
    event.waitUntil(activateFreshClient());
});

async function activateFreshClient() {
    await deleteOfflineCaches();
    await self.clients.claim();
    await self.registration.unregister();

    const clients = await self.clients.matchAll({ type: 'window' });
    await Promise.all(clients.map(async client => {
        try {
            await client.navigate(client.url);
        } catch {
            // The client may already be closing; cache cleanup still succeeded.
        }
    }));
}

async function deleteOfflineCaches() {
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys
            .filter(key => key.startsWith(cacheNamePrefix))
            .map(key => caches.delete(key)));
}
