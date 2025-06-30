using SHINE.Core;
using System.Net.Sockets;
using System.Net;

namespace SHINE.Server
{

    public class UdpShineConnection : IShineConnection
    {
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _remoteEndPoint;

        public UdpShineConnection(UdpClient udpClient, IPEndPoint remoteEndPoint)
        {
            _udpClient = udpClient;
            _remoteEndPoint = remoteEndPoint;
        }

        public bool IsConnected => true;

        public Task SendAsync(IMessage message)
        {
            try
            {
                var data = message.Serialize();
                return _udpClient.SendAsync(data, data.Length, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                Logger.Log.Error(ex, $"UDP send to {_remoteEndPoint} failed");
                return Task.CompletedTask;
            }
        }

        public Task<IMessage?> ReceiveAsync() =>
            Task.FromResult<IMessage?>(null);

        public Task DisconnectAsync() => Task.CompletedTask;
    }
}
