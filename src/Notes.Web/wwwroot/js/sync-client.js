// WebSocket sync client
let ws = null;

export function connect(url, room, dotNetRef, onMessageMethod, onStatusMethod) {
    try {
        ws = new WebSocket(url);
    } catch (e) {
        dotNetRef.invokeMethodAsync(onStatusMethod, `Connection error: ${e.message}`);
        return;
    }

    ws.onopen = () => {
        ws.send(JSON.stringify({ room }));
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
