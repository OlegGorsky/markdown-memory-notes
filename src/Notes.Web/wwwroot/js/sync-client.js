// WebSocket sync client
let ws = null;
let connection = null;
let reconnectTimer = null;
let reconnectTimerClear = null;
let reconnectAttempts = 0;

const defaultReconnectOptions = {
    initialReconnectDelayMs: 500,
    maxReconnectDelayMs: 10000,
    reconnectJitterRatio: 0.2
};

export function getDefaultSyncUrl() {
    const configured = window.localStorage.getItem('mmn-sync-url');
    if (configured) return configured;

    const host = window.location.hostname;
    const isLocal = host === 'localhost' || host === '127.0.0.1' || host === '::1';
    if (isLocal) return 'ws://localhost:5199/sync';

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    return `${protocol}//${window.location.host}/sync`;
}

export function connect(url, room, dotNetRef, onMessageMethod, onStatusMethod, options = {}) {
    stopReconnectTimer();
    closeCurrentSocket();
    reconnectAttempts = 0;
    connection = {
        url,
        room,
        dotNetRef,
        onMessageMethod,
        onStatusMethod,
        options: normalizeOptions(options),
        reconnect: true
    };

    openSocket(connection);
}

function openSocket(state) {
    if (!state.reconnect) return;

    const WebSocketType = state.options.WebSocket;
    let socket;
    try {
        socket = new WebSocketType(state.url);
    } catch (e) {
        notifyStatus(state, `Connection error: ${e.message}`);
        scheduleReconnect(state);
        return;
    }

    ws = socket;

    socket.onopen = () => {
        if (!isCurrentSocket(state, socket)) return;

        try {
            socket.send(JSON.stringify({ room: state.room }));
            reconnectAttempts = 0;
            notifyStatus(state, 'connected');
        } catch (e) {
            notifyStatus(state, `Connection error: ${e.message}`);
            closeCurrentSocket();
            scheduleReconnect(state);
        }
    };

    socket.onmessage = (event) => {
        if (!isCurrentSocket(state, socket)) return;
        state.dotNetRef.invokeMethodAsync(state.onMessageMethod, event.data);
    };

    socket.onclose = () => {
        if (ws === socket) {
            ws = null;
        }

        if (connection !== state) return;

        notifyStatus(state, 'disconnected');
        if (state.reconnect) {
            scheduleReconnect(state);
        }
    };

    socket.onerror = () => {
        if (!isCurrentSocket(state, socket)) return;
        notifyStatus(state, 'error');
    };
}

export function send(data) {
    const socket = ws;
    const openState = connection?.options.WebSocket.OPEN ?? 1;
    if (socket && socket.readyState === openState) {
        try {
            socket.send(data);
            return true;
        } catch {
            if (connection) {
                notifyStatus(connection, 'error');
                const state = connection;
                closeCurrentSocket();
                scheduleReconnect(state);
            }

            return false;
        }
    }

    return false;
}

export function disconnect() {
    if (connection) {
        connection.reconnect = false;
    }

    stopReconnectTimer();
    if (ws) {
        closeCurrentSocket();
    }

    connection = null;
}

function scheduleReconnect(state) {
    if (!state.reconnect || reconnectTimer) return;

    const delay = nextReconnectDelay(state.options);
    reconnectTimerClear = state.options.clearTimeout;
    reconnectTimer = state.options.setTimeout(() => {
        reconnectTimer = null;
        reconnectTimerClear = null;
        if (connection === state && state.reconnect) {
            openSocket(state);
        }
    }, delay);
}

function nextReconnectDelay(options) {
    const baseDelay = Math.min(
        options.initialReconnectDelayMs * 2 ** reconnectAttempts,
        options.maxReconnectDelayMs);
    reconnectAttempts++;

    const jitterRatio = options.reconnectJitterRatio;
    const jitter = 1 + ((options.random() * 2) - 1) * jitterRatio;
    return Math.min(
        options.maxReconnectDelayMs,
        Math.max(0, Math.round(baseDelay * jitter)));
}

function stopReconnectTimer() {
    if (reconnectTimer && reconnectTimerClear) {
        reconnectTimerClear(reconnectTimer);
    }

    reconnectTimer = null;
    reconnectTimerClear = null;
}

function closeCurrentSocket() {
    const socket = ws;
    ws = null;
    if (!socket) return;

    socket.onopen = null;
    socket.onmessage = null;
    socket.onclose = null;
    socket.onerror = null;
    socket.close();
}

function isCurrentSocket(state, socket) {
    return connection === state && ws === socket;
}

function notifyStatus(state, status) {
    state.dotNetRef.invokeMethodAsync(state.onStatusMethod, status);
}

function normalizeOptions(options) {
    return {
        WebSocket: options.WebSocket ?? globalThis.WebSocket,
        setTimeout: options.setTimeout ?? ((callback, delay) => globalThis.setTimeout(callback, delay)),
        clearTimeout: options.clearTimeout ?? (timer => globalThis.clearTimeout(timer)),
        random: options.random ?? Math.random,
        initialReconnectDelayMs: positiveNumberOrDefault(
            options.initialReconnectDelayMs,
            defaultReconnectOptions.initialReconnectDelayMs),
        maxReconnectDelayMs: positiveNumberOrDefault(
            options.maxReconnectDelayMs,
            defaultReconnectOptions.maxReconnectDelayMs),
        reconnectJitterRatio: nonNegativeNumberOrDefault(
            options.reconnectJitterRatio,
            defaultReconnectOptions.reconnectJitterRatio)
    };
}

function positiveNumberOrDefault(value, fallback) {
    return Number.isFinite(value) && value > 0 ? value : fallback;
}

function nonNegativeNumberOrDefault(value, fallback) {
    return Number.isFinite(value) && value >= 0 ? value : fallback;
}
