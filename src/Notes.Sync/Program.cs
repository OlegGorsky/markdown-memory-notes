using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
#pragma warning disable CA1812 // Instantiated via JSON deserialization
#pragma warning disable CA1852 // Sealed for analyzer

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// Room -> list of connected WebSockets
var rooms = new ConcurrentDictionary<string, ConcurrentBag<WebSocket>>();
var socketRooms = new ConcurrentDictionary<WebSocket, string>();

app.Map("/sync", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket required");
        return;
    }

    var ws = await context.WebSockets.AcceptWebSocketAsync();
    string? room = null;

    try
    {
        // First message must be the room code
        var joinMsg = await ReceiveAsync(ws);
        var join = JsonSerializer.Deserialize<JoinMessage>(joinMsg);
        if (join?.Room is not { Length: > 0 })
        {
            await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Room required", CancellationToken.None);
            return;
        }

        room = join.Room;
        var bag = rooms.GetOrAdd(room, _ => new ConcurrentBag<WebSocket>());
        bag.Add(ws);
        socketRooms[ws] = room;

        Console.WriteLine($"[+] {room}: connected ({bag.Count} peers)");

        // Relay loop: receive from one, broadcast to others in room
        var buffer = new byte[1024 * 64];
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var segment = new ArraySegment<byte>(buffer, 0, result.Count);
            if (rooms.TryGetValue(room, out var peers))
            {
                foreach (var peer in peers)
                {
                    if (peer != ws && peer.State == WebSocketState.Open)
                    {
                        await peer.SendAsync(segment, WebSocketMessageType.Text, result.EndOfMessage, CancellationToken.None);
                    }
                }
            }
        }
    }
    catch (WebSocketException) { }
    finally
    {
        if (room is not null && rooms.TryGetValue(room, out var bag))
        {
            // Remove self from room
            var list = bag.ToList();
            list.Remove(ws);
            rooms[room] = new ConcurrentBag<WebSocket>(list);
            Console.WriteLine($"[-] {room}: disconnected ({list.Count} peers)");
        }
        socketRooms.TryRemove(ws, out _);
        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        ws.Dispose();
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", rooms = rooms.Count }));

app.Run("http://0.0.0.0:5199");

static async Task<string> ReceiveAsync(WebSocket ws)
{
    var buffer = new byte[4096];
    var sb = new StringBuilder();
    while (true)
    {
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        if (result.EndOfMessage) break;
    }
    return sb.ToString();
}

sealed record JoinMessage(string Room);
