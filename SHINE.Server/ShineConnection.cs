using System.Net.WebSockets;
using SHINE.Core;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SHINE.Server;

public class ShineConnection : IShineConnection, IDisposable
{
    private readonly WebSocket _webSocket;
    private readonly Channel<IMessage> _sendChannel = Channel.CreateUnbounded<IMessage>();
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;

    public ShineConnection(WebSocket webSocket)
    {
        _webSocket = webSocket;
        // Запускаем фоновую задачу отправки
        _ = Task.Run(() => SendLoopAsync(_cts.Token));
    }

    public bool IsConnected => !_isDisposed && _webSocket.State == WebSocketState.Open;

    public async Task SendAsync(IMessage message)
    {
        if (!IsConnected) return;
        await _sendChannel.Writer.WriteAsync(message, _cts.Token);
    }

    private async Task SendLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var message in _sendChannel.Reader.ReadAllAsync(token))
            {
                try
                {
                    var data = message.Serialize();
                    await _webSocket.SendAsync(
                        data,
                        WebSocketMessageType.Binary,
                        true,
                        token
                    );
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (IsIgnorableSocketError(ex))
                {
                    Logger.Log.Debug("Send error: {Message}", ex.Message);
                    break;
                }
            }
        }
        finally
        {
            SafeCleanup();
        }
    }

    public async Task<IMessage?> ReceiveAsync()
    {
        try
        {
            var buffer = new ArraySegment<byte>(new byte[65535]);
            using var ms = new MemoryStream();

            while (true)
            {
                var result = await _webSocket.ReceiveAsync(buffer, _cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                ms.Write(buffer.Array!, buffer.Offset, result.Count);

                if (result.EndOfMessage)
                {
                    var message = new Message();
                    message.Deserialize(ms.ToArray());
                    return message;
                }
            }
        }
        catch (Exception ex) when (IsIgnorableSocketError(ex))
        {
            Logger.Log.Debug("Receive error: {Message}", ex.Message);
            return null;
        }
    }

    private bool IsIgnorableSocketError(Exception ex)
    {
        return ex is OperationCanceledException
            || ex is WebSocketException
            || ex is ObjectDisposedException;
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnect",
                    CancellationToken.None
                );
            }
        }
        catch { }
        finally
        {
            SafeCleanup();
        }
    }

    private void SafeCleanup()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _cts.Cancel();
            _webSocket.Dispose();
            _sendChannel.Writer.TryComplete();
        }
        catch { }
    }

    public void Dispose() => SafeCleanup();
}
