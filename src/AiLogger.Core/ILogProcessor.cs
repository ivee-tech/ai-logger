using System.Threading;
using System.Threading.Tasks;

namespace AiLogger.Core
{
    public interface ILogProcessor
    {
        Task ProcessAsync(CancellationToken cancellationToken = default);
    }
}
