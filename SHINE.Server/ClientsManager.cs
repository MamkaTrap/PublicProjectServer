using System.Collections.Concurrent;
using System.Net;
using SHINE.Core;

namespace SHINE.Server;

public class ClientsManager : IClientsManager
{
    private readonly ConcurrentDictionary<string, ClientRecord> _clients = new();

    public void AddClient(string clientId, IShineConnection connection)
    {
        _clients[clientId] = new ClientRecord(connection);
    }

    public void RemoveClient(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    public bool TryGetClient(string clientId, out IShineConnection connection)
    {
        if (_clients.TryGetValue(clientId, out var record))
        {
            connection = record.Connection;
            return true;
        }
        connection = null;
        return false;
    }

    public void UpdateUdpEndPoint(string clientId, IPEndPoint endPoint)
    {
        if (_clients.TryGetValue(clientId, out var record))
        {
            record.UdpEndPoint = endPoint;
        }
    }

    public bool TryGetUdpEndPoint(string clientId, out IPEndPoint endPoint)
    {
        if (_clients.TryGetValue(clientId, out var record) && record.UdpEndPoint != null)
        {
            endPoint = record.UdpEndPoint;
            return true;
        }
        endPoint = null;
        return false;
    }

    public IEnumerable<string> GetAllClients() => _clients.Keys;

    public IReadOnlyDictionary<string, IShineConnection> AllClients =>
        _clients.ToDictionary(p => p.Key, p => p.Value.Connection);

    private class ClientRecord
    {
        public IShineConnection Connection { get; }
        public IPEndPoint UdpEndPoint { get; set; }

        public ClientRecord(IShineConnection connection)
        {
            Connection = connection;
        }
    }
}
