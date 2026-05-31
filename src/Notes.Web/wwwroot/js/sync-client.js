// WebSocket sync client
let ws = null;

export function getDefaultSyncUrl() {
    const configured = window.localStorage.getItem('mmn-sync-url');
    if (configured) return configured;

    const host = window.location.hostname;
    const isLocal = host === 'localhost' || host === '127.0.0.1' || host === '::1';
    if (isLocal) return 'ws://localhost:5199/sync';

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    return `${protocol}//${window.location.host}/sync`;
}

export function connect(url, room, dotNetRef, onMessageMethod, onStatusMethod) {
    try {
        ws = new WebSocket(url);
    } catch (e) {
        dotNetRef.invokeMethodAsync(onStatusMethod, `Connection error: ${e.message}`);
        return;
    }

    ws.onopen = () => {
        ws.send(JSON.stringify({ room }));
        dotNetRef.invokeMethodAsync(onStatusMethod, 'connected');
    };

    ws.onmessage = (event) => {
        dotNetRef.invokeMethodAsync(onMessageMethod, event.data);
    };

    ws.onclose = () => {
        dotNetRef.invokeMethodAsync(onStatusMethod, 'disconnected');
        ws = null;
    };

    ws.onerror = () => {
        dotNetRef.invokeMethodAsync(onStatusMethod, 'error');
    };
}

export function send(data) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(data);
    }
}

export function disconnect() {
    if (ws) {
        ws.close();
        ws = null;
    }
}
