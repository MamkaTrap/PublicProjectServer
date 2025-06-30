using SHINE.Core;
using SHINE.Server;

public class ShineServerBuilder
{
    private string? _wsPrefix;
    private int? _udpPort;
    private IDataDispatcher? _dispatcher;
    private bool _debugMode = false;


    public ShineServerBuilder WithWebSocketPrefix(string wsPrefix)
    {
        _wsPrefix = wsPrefix;
        return this;
    }

    public ShineServerBuilder DebugMode(bool active)
    {
        _debugMode = active;
        return this;
    }

    public ShineServerBuilder WithUdpPort(int port)
    {
        if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _udpPort = port;
        return this;
    }

    public ShineServerBuilder WithDispatcher(IDataDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        return this;
    }

    public ShineServer Build()
    {
        if (string.IsNullOrEmpty(_wsPrefix))
            throw new InvalidOperationException("WebSocket prefix is required.");
        if (!_udpPort.HasValue)
            throw new InvalidOperationException("UDP port is required.");
        if (_dispatcher == null)
            throw new InvalidOperationException("Dispatcher is required.");

        return new ShineServer(_wsPrefix!, _udpPort.Value, _debugMode, _dispatcher!);
    }
}
