using AiLogger.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AiLogger.Providers
{
    public interface IAIProviderFactory
    {
        IAIProvider GetProvider();
    }

    public class AIProviderFactory : IAIProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public AIProviderFactory(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
        }

        public IAIProvider GetProvider()
        {
            var defaultProvider = _configuration.GetValue<string>("AiProviders:Default");
            var providers = _serviceProvider.GetServices<IAIProvider>();

            return providers.FirstOrDefault(p => p.ProviderName.Equals(defaultProvider, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Default AI provider '{defaultProvider}' not found or configured.");
        }
    }
}
