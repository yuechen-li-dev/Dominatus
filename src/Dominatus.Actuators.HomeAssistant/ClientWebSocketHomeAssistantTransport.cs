using System.Net.WebSockets;
using System.Text;

namespace Dominatus.Actuators.HomeAssistant;

public sealed class ClientWebSocketHomeAssistantTransport : IHomeAssistantWebSocketTransport
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        => _socket.ConnectAsync(uri, cancellationToken);

    public Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    public async Task<string?> ReceiveTextAsync(int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];

        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(chunk), cancellationToken).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidOperationException("Home Assistant WebSocket returned a non-text frame.");

            if (result.Count > 0)
            {
                if (buffer.Length + result.Count > maxBytes)
                    throw new InvalidOperationException($"Home Assistant WebSocket message exceeds MaxMessageBytes ({maxBytes}).");

                buffer.Write(chunk, 0, result.Count);
            }

            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
