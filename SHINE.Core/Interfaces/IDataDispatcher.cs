using System.Threading.Tasks;

namespace SHINE.Core
{

    public interface IDataDispatcher
    {
        Task DispatchAsync(IMessage message, IShineConnection connection);
    }
}
