using AiLogger.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace AiLogger.Providers
{
    public abstract class AIProviderBase : IAIProvider
    {
        protected readonly ILogger _logger;

        protected AIProviderBase(ILogger logger)
        {
            _logger = logger;
        }

        public abstract string ProviderName { get; }
        public abstract bool IsConfigured { get; }

        public abstract Task<string> AnalyzeTextAsync(string text, SensitiveDataOptions options, CancellationToken cancellationToken = default);
        public abstract Task<SanitizationResult> SanitizeAsync(string text, SanitizationOptions options, CancellationToken cancellationToken = default);
    }
}
