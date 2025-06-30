using System.Collections.Generic;
using System.Net;

namespace SHINE.Core
{
    public interface IClientsManager
    {
        void AddClient(string clientId, IShineConnection connection);
        void RemoveClient(string clientId);
        bool TryGetClient(string clientId, out IShineConnection connection);
        IEnumerable<string> GetAllClients();
        IReadOnlyDictionary<string, IShineConnection> AllClients { get; }
        void UpdateUdpEndPoint(string clientId, IPEndPoint endPoint);
    }
}


