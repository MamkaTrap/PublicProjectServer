using SHINE.Core;

namespace SHINE.Server;

public static class Broadcast
{
    private static IClientsManager? _clientsManager;

    public static void Initialize(IClientsManager clientsManager)
    {
        _clientsManager = clientsManager;
    }

    public static async Task SendToClientAsync(string clientId, IMessage message)
    {
        if (_clientsManager == null) return;

        if (_clientsManager.TryGetClient(clientId, out var conn) && conn.IsConnected)
        {
            await conn.SendAsync(message);
        }
    }

    public static async Task BroadcastAsync(IMessage message, string? excludeClientId = null)
    {
        if (_clientsManager == null) return;

        var tasks = new List<Task>();

        foreach (var (id, conn) in _clientsManager.AllClients)
        {
            if (id != excludeClientId && conn.IsConnected)
            {
                tasks.Add(conn.SendAsync(message));
            }
        }

        await Task.WhenAll(tasks);
    }

    public static IReadOnlyDictionary<string, IShineConnection> Clients =>
        _clientsManager?.AllClients ?? new Dictionary<string, IShineConnection>();
}