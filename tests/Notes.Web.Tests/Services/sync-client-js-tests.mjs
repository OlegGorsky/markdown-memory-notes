import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const modulePath = new URL('../../../src/Notes.Web/wwwroot/js/sync-client.js', import.meta.url);

async function loadSyncClient() {
  const source = await readFile(modulePath, 'utf8');
  return import(`data:text/javascript,${encodeURIComponent(source)}#${Date.now()}-${Math.random()}`);
}

function createHarness() {
  const timers = [];

  class FakeWebSocket {
    static CONNECTING = 0;
    static OPEN = 1;
    static CLOSED = 3;
    static instances = [];

    constructor(url) {
      this.url = url;
      this.readyState = FakeWebSocket.CONNECTING;
      this.sent = [];
      FakeWebSocket.instances.push(this);
    }

    open() {
      this.readyState = FakeWebSocket.OPEN;
      this.onopen?.();
    }

    close() {
      this.readyState = FakeWebSocket.CLOSED;
      this.onclose?.();
    }

    serverClose() {
      this.close();
    }

    send(data) {
      assert.equal(this.readyState, FakeWebSocket.OPEN);
      if (this.throwOnSend) {
        throw new Error('send failed');
      }

      this.sent.push(data);
    }
  }

  const dotNetRef = {
    statuses: [],
    messages: [],
    invokeMethodAsync(method, arg) {
      if (method === 'OnStatus') {
        this.statuses.push(arg);
      } else if (method === 'OnMessage') {
        this.messages.push(arg);
      }

      return Promise.resolve();
    }
  };

  const options = {
    WebSocket: FakeWebSocket,
    setTimeout(callback, delay) {
      const timer = { callback, delay, cleared: false };
      timers.push(timer);
      return timer;
    },
    clearTimeout(timer) {
      timer.cleared = true;
    },
    random: () => 0.5,
    initialReconnectDelayMs: 100,
    maxReconnectDelayMs: 500
  };

  return {
    FakeWebSocket,
    dotNetRef,
    timers,
    options,
    runNextTimer() {
      const timer = timers.find(candidate => !candidate.cleared);
      assert.ok(timer, 'expected a scheduled reconnect timer');
      timer.cleared = true;
      timer.callback();
      return timer;
    },
    activeTimers() {
      return timers.filter(timer => !timer.cleared);
    }
  };
}

async function testReconnectsAfterUnexpectedClose() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  assert.deepEqual(JSON.parse(harness.FakeWebSocket.instances[0].sent[0]), { room: 'AbCdEfGhIjKlMnOpQrStUv' });
  assert.deepEqual(harness.dotNetRef.statuses, ['connected']);

  harness.FakeWebSocket.instances[0].serverClose();

  assert.equal(harness.dotNetRef.statuses.at(-1), 'disconnected');
  assert.equal(harness.activeTimers()[0].delay, 100);

  harness.runNextTimer();
  assert.equal(harness.FakeWebSocket.instances.length, 2);
  harness.FakeWebSocket.instances[1].open();

  assert.deepEqual(JSON.parse(harness.FakeWebSocket.instances[1].sent[0]), { room: 'AbCdEfGhIjKlMnOpQrStUv' });
  assert.equal(harness.dotNetRef.statuses.at(-1), 'connected');
}

async function testDisconnectStopsReconnects() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  syncClient.disconnect();

  assert.equal(harness.FakeWebSocket.instances[0].readyState, harness.FakeWebSocket.CLOSED);
  assert.equal(harness.activeTimers().length, 0);
}

async function testReconnectBackoffIsBounded() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  harness.FakeWebSocket.instances[0].serverClose();
  assert.equal(harness.activeTimers().at(-1).delay, 100);

  harness.runNextTimer();
  harness.FakeWebSocket.instances[1].serverClose();
  assert.equal(harness.activeTimers().at(-1).delay, 200);

  harness.runNextTimer();
  harness.FakeWebSocket.instances[2].serverClose();
  assert.equal(harness.activeTimers().at(-1).delay, 400);

  harness.runNextTimer();
  harness.FakeWebSocket.instances[3].serverClose();
  assert.equal(harness.activeTimers().at(-1).delay, 500);
}

async function testSendFailureStartsReconnect() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();
  harness.FakeWebSocket.instances[0].throwOnSend = true;

  assert.equal(syncClient.send('payload'), false);
  assert.equal(harness.dotNetRef.statuses.at(-1), 'error');
  assert.equal(harness.activeTimers()[0].delay, 100);

  harness.runNextTimer();
  assert.equal(harness.FakeWebSocket.instances.length, 2);
}

const tests = [
  testReconnectsAfterUnexpectedClose,
  testDisconnectStopsReconnects,
  testReconnectBackoffIsBounded,
  testSendFailureStartsReconnect
];

for (const test of tests) {
  await test();
}

console.log(`sync-client-js-tests: ${tests.length} passed`);
