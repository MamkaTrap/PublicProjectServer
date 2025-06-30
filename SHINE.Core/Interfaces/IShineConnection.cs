using System.Threading.Tasks;

namespace SHINE.Core
{

    public interface IShineConnection
    {
        Task SendAsync(IMessage message);
        Task<IMessage?> ReceiveAsync();
        Task DisconnectAsync();
        bool IsConnected { get; }
    }

}
