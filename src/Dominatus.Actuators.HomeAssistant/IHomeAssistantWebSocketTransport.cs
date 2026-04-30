namespace Dominatus.Actuators.HomeAssistant;

public interface IHomeAssistantWebSocketTransport : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task SendTextAsync(string text, CancellationToken cancellationToken);
    Task<string?> ReceiveTextAsync(int maxBytes, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}
