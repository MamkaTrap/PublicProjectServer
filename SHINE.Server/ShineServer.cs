using SHINE.Core;
using System.Net;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading;

namespace SHINE.Server;

public class ShineServer
{
    private readonly HttpListener _listener;
    private readonly UdpClient _udpClient;
    private readonly string _wsPrefix;
    private readonly int _udpPort;
    private readonly IClientsManager _clientsManager;
    private readonly IDataDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<(IMessage, IShineConnection)> _messageChannel;
    private readonly SemaphoreSlim _dispatchSemaphore = new(Environment.ProcessorCount * 4);
    private long _messageCount = 0;


    internal ShineServer(string uriPrefix, int udpPort, bool debugMode, IDataDispatcher dispatcher)
    {
        _wsPrefix = uriPrefix;
        _udpPort = udpPort;

        _listener = new HttpListener();
        _listener.Prefixes.Add(uriPrefix);

        _udpClient = new UdpClient(udpPort);

        _clientsManager = new ClientsManager();
        _dispatcher = dispatcher;
        _cts = new CancellationTokenSource();
        _messageChannel = Channel.CreateUnbounded<(IMessage, IShineConnection)>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        Broadcast.Initialize(_clientsManager);
        Logger.Log.Debug("Shine Server Starting...");
        Logger.Log.Debug($"Server WS Prefix: {uriPrefix}");
        Logger.Log.Debug($"UDP Port: {_udpClient.Client.LocalEndPoint}");

        if (debugMode)
        {
            _ = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(1000, _cts.Token);
                    var count = Interlocked.Exchange(ref _messageCount, 0);
                    Logger.Log.Information($"Messages/sec: {count}");
                }
            }, _cts.Token);
        }
        else
        {
            Logger.Log.Debug("Debug mode is off, no message statistics.");
        }
    }

    public static ShineServerBuilder Create() => new ShineServerBuilder();

    public async Task StartAsync()
    {
        try
        {
            _listener.Start();
            Logger.Log.Information("Server started...");
            _ = Task.Run(() => MessageProcessingLoopAsync(_cts.Token), _cts.Token);
            _ = Task.Run(() => ProcessUdpLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            Logger.Log.Error(ex, "StartAsync failed.");
            throw;
        }

        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? httpContext = null;
            try
            {
                httpContext = await _listener.GetContextAsync();

                if (httpContext.Request.IsWebSocketRequest)
                    _ = ProcessWebSocketAsync(httpContext);
                else
                {
                    httpContext.Response.StatusCode = 400;
                    httpContext.Response.Close();
                }
            }
            catch (HttpListenerException ex)
            {
                Logger.Log.Error(ex, "Listener exception");
                httpContext?.Response?.Close();
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex, "Unexpected exception");
                httpContext?.Response?.Close();
            }
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _udpClient.Close();

        _messageChannel.Writer.Complete();
        _dispatchSemaphore.Dispose();

        Logger.Log.Information("Server stopped.");
    }

    private async Task ProcessUdpLoopAsync(CancellationToken token)
    {
        Logger.Log.Information("UDP listener started.");
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _udpClient.ReceiveAsync();
                var data = result.Buffer;
                IMessage msg = result.Buffer.Deserialize();
                IShineConnection udpConn = new UdpShineConnection(_udpClient, result.RemoteEndPoint);

                Interlocked.Increment(ref _messageCount);
                _ = Task.Run(async () =>
                {
                    await _dispatchSemaphore.WaitAsync(token);
                    try
                    {
                        await _dispatcher.DispatchAsync(msg, udpConn);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log.Error(ex, "UDP message dispatch failed");
                    }
                    finally
                    {
                        _dispatchSemaphore.Release();
                    }
                }, token);
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Logger.Log.Error(ex, "UDP processing loop crashed");
        }
    }

    private async Task ProcessWebSocketAsync(HttpListenerContext context)
    {
        WebSocketContext? wsContext = null;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(null);
            var webSocket = wsContext.WebSocket;
            var connection = new ShineConnection(webSocket);

            var clientId = Guid.NewGuid().ToString();
            _clientsManager.AddClient(clientId, connection);

            await ReceiveLoopAsync(connection, clientId);
        }
        catch (WebSocketException ex)
        {
            Logger.Log.Error(ex, "WebSocket exception");
        }
        catch (Exception ex)
        {
            Logger.Log.Error(ex, "Unexpected error in ProcessWebSocketAsync");
        }
    }

    private async Task ReceiveLoopAsync(IShineConnection connection, string clientId)
    {
        try
        {
            while (connection.IsConnected)
            {
                var msg = await connection.ReceiveAsync();
                if (msg != null)
                {
                    msg.SenderId = clientId;
                    await _messageChannel.Writer.WriteAsync((msg, connection));
                }
                else break;
            }
        }
        catch (WebSocketException ex)
        {
            Logger.Log.Error(ex, $"WebSocket receive exception from client {clientId}");
        }
        catch (Exception ex)
        {
            Logger.Log.Debug(ex, "Receive loop error for client {ClientId}", clientId);
        }
        finally
        {
            _clientsManager.RemoveClient(clientId);
            await connection.DisconnectAsync();
        }
    }

    private async Task MessageProcessingLoopAsync(CancellationToken token)
    {
        try
        {
            var reader = _messageChannel.Reader;
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var item))
                {
                    Interlocked.Increment(ref _messageCount);

                    _ = Task.Run(async () =>
                    {
                        await _dispatchSemaphore.WaitAsync(token);
                        try
                        {
                            await _dispatcher.DispatchAsync(item.Item1, item.Item2);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log.Error(ex, "Message dispatch failed");
                        }
                        finally
                        {
                            _dispatchSemaphore.Release();
                        }
                    }, token);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Log.Error(ex, "MessageProcessingLoop crashed");
        }
    }
}
