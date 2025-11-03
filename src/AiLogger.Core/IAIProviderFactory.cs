namespace AiLogger.Core;

public interface IAIProviderFactory
{
    IAIProvider GetProvider(string? preferredProviderName = null);
}
