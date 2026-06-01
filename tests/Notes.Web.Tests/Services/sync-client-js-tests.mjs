import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const modulePath = new URL('../../../src/Notes.Web/wwwroot/js/sync-client.js', import.meta.url);

async function loadSyncClient() {
  const source = await readFile(modulePath, 'utf8');
  return import(`data:text/javascript,${encodeURIComponent(source)}#${Date.now()}-${Math.random()}`);
}

function createHarness() {
  const timers = [];
  const intervals = [];
  const pendingInvocations = [];

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
    holdMessages: false,
    rejectNextMessage: false,
    rejectStatuses: false,
    pendingInvocations,
    invokeMethodAsync(method, arg) {
      if (method === 'OnStatus') {
        this.statuses.push(arg);
        if (this.rejectStatuses) {
          return Promise.reject(new Error('status handler failed'));
        }
      } else if (method === 'OnMessage') {
        this.messages.push(arg);
        if (this.rejectNextMessage) {
          this.rejectNextMessage = false;
          return Promise.reject(new Error('message handler failed'));
        }

        if (this.holdMessages) {
          let resolve;
          const promise = new Promise(done => {
            resolve = done;
          });
          pendingInvocations.push({ method, arg, resolve });
          return promise;
        }
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
    setInterval(callback, delay) {
      const interval = { callback, delay, cleared: false };
      intervals.push(interval);
      return interval;
    },
    clearInterval(interval) {
      interval.cleared = true;
    },
    random: () => 0.5,
    initialReconnectDelayMs: 100,
    maxReconnectDelayMs: 500
  };

  return {
    FakeWebSocket,
    dotNetRef,
    pendingInvocations,
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
    },
    runNextInterval() {
      const interval = intervals.find(candidate => !candidate.cleared);
      assert.ok(interval, 'expected a scheduled heartbeat interval');
      interval.callback();
      return interval;
    },
    activeIntervals() {
      return intervals.filter(interval => !interval.cleared);
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

async function testIncomingMessagesAreDeliveredSequentially() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();
  harness.dotNetRef.holdMessages = true;

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  harness.FakeWebSocket.instances[0].onmessage({ data: 'first' });
  harness.FakeWebSocket.instances[0].onmessage({ data: 'second' });
  await nextTurn();

  assert.deepEqual(harness.dotNetRef.messages, ['first']);

  harness.pendingInvocations[0].resolve();
  await nextTurn();

  assert.deepEqual(harness.dotNetRef.messages, ['first', 'second']);
}

async function testIncomingMessageFailureReportsErrorAndContinues() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();
  harness.dotNetRef.rejectNextMessage = true;

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  harness.FakeWebSocket.instances[0].onmessage({ data: 'bad' });
  harness.FakeWebSocket.instances[0].onmessage({ data: 'next' });
  await nextTurn();

  assert.equal(harness.dotNetRef.statuses.at(-1), 'error');
  assert.deepEqual(harness.dotNetRef.messages, ['bad', 'next']);
}

async function testIncomingQueueOverflowReconnectsInsteadOfGrowingUnbounded() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();
  harness.dotNetRef.holdMessages = true;
  harness.options.maxIncomingMessages = 2;

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  harness.FakeWebSocket.instances[0].onmessage({ data: 'first' });
  await nextTurn();
  harness.FakeWebSocket.instances[0].onmessage({ data: 'second' });
  harness.FakeWebSocket.instances[0].onmessage({ data: 'third' });
  await nextTurn();

  assert.equal(harness.dotNetRef.statuses.at(-1), 'overloaded');
  assert.equal(harness.FakeWebSocket.instances[0].readyState, harness.FakeWebSocket.CLOSED);
  assert.equal(harness.activeTimers()[0].delay, 100);
  assert.deepEqual(harness.dotNetRef.messages, ['first']);

  harness.pendingInvocations[0].resolve();
  await nextTurn();
  assert.deepEqual(harness.dotNetRef.messages, ['first']);

  harness.runNextTimer();
  assert.equal(harness.FakeWebSocket.instances.length, 2);
}

async function testStatusCallbackFailuresDoNotCreateUnhandledRejections() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();
  harness.dotNetRef.rejectStatuses = true;

  const unhandled = await didUnhandledRejectionOccurDuring(() => {
    syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
      harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
    harness.FakeWebSocket.instances[0].open();
  });

  assert.equal(unhandled, false);
}

async function testHeartbeatRunsWhileSocketIsOpen() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();
  harness.options.heartbeatIntervalMs = 1000;

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  assert.equal(harness.activeIntervals().length, 1);
  assert.equal(harness.activeIntervals()[0].delay, 1000);

  harness.runNextInterval();

  assert.deepEqual(JSON.parse(harness.FakeWebSocket.instances[0].sent.at(-1)), { type: 'heartbeat' });
}

async function testHeartbeatIntervalUsesJitter() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();
  harness.options.heartbeatIntervalMs = 1000;
  harness.options.heartbeatJitterRatio = 0.2;
  harness.options.random = () => 0;

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();

  assert.equal(harness.activeIntervals()[0].delay, 800);
}

async function testHeartbeatStopsOnDisconnect() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();
  assert.equal(harness.activeIntervals().length, 1);

  syncClient.disconnect();

  assert.equal(harness.activeIntervals().length, 0);
}

async function testHeartbeatStopsAfterUnexpectedCloseAndRestartsOnReconnect() {
  const syncClient = await loadSyncClient();
  const harness = createHarness();

  syncClient.connect('ws://localhost/sync', 'AbCdEfGhIjKlMnOpQrStUv',
    harness.dotNetRef, 'OnMessage', 'OnStatus', harness.options);
  harness.FakeWebSocket.instances[0].open();
  assert.equal(harness.activeIntervals().length, 1);

  harness.FakeWebSocket.instances[0].serverClose();

  assert.equal(harness.activeIntervals().length, 0);

  harness.runNextTimer();
  harness.FakeWebSocket.instances[1].open();

  assert.equal(harness.activeIntervals().length, 1);
}

function nextTurn() {
  return new Promise(resolve => setImmediate(resolve));
}

async function didUnhandledRejectionOccurDuring(action) {
  let handler;
  const unhandled = new Promise(resolve => {
    handler = () => resolve(true);
    process.once('unhandledRejection', handler);
  });

  try {
    action();
    return await Promise.race([
      unhandled,
      nextTurn().then(nextTurn).then(() => false)
    ]);
  } finally {
    process.removeListener('unhandledRejection', handler);
  }
}

const tests = [
  testReconnectsAfterUnexpectedClose,
  testDisconnectStopsReconnects,
  testReconnectBackoffIsBounded,
  testSendFailureStartsReconnect,
  testIncomingMessagesAreDeliveredSequentially,
  testIncomingMessageFailureReportsErrorAndContinues,
  testIncomingQueueOverflowReconnectsInsteadOfGrowingUnbounded,
  testStatusCallbackFailuresDoNotCreateUnhandledRejections,
  testHeartbeatRunsWhileSocketIsOpen,
  testHeartbeatIntervalUsesJitter,
  testHeartbeatStopsOnDisconnect,
  testHeartbeatStopsAfterUnexpectedCloseAndRestartsOnReconnect
];

for (const test of tests) {
  await test();
}

console.log(`sync-client-js-tests: ${tests.length} passed`);
