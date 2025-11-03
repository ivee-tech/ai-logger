using AiLogger.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AiLogger.Providers
{
    public class AIProviderFactory : IAIProviderFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AIProviderFactory> _logger;

        public AIProviderFactory(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<AIProviderFactory> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        public IAIProvider GetProvider(string? preferredProviderName = null)
        {
            var providers = _serviceProvider.GetServices<IAIProvider>().ToList();
            if (providers.Count == 0)
            {
                throw new InvalidOperationException("No AI providers are registered in the service container.");
            }

            var seenCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var defaultProviderName = _configuration.GetValue<string>("AiProviders:Default");

            IEnumerable<string?> CandidateNames()
            {
                if (!string.IsNullOrWhiteSpace(preferredProviderName))
                {
                    yield return preferredProviderName;
                }

                if (!string.IsNullOrWhiteSpace(defaultProviderName) && !string.Equals(defaultProviderName, preferredProviderName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return defaultProviderName;
                }
            }

            foreach (var candidate in CandidateNames())
            {
                if (string.IsNullOrWhiteSpace(candidate) || !seenCandidates.Add(candidate))
                {
                    continue;
                }

                var match = providers.FirstOrDefault(p =>
                    p.ProviderName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (match.IsConfigured)
                    {
                        _logger.LogInformation("Using AI provider '{ProviderName}'.", match.ProviderName);
                        return match;
                    }

                    _logger.LogWarning(
                        "AI provider '{ProviderName}' is registered but not fully configured. Attempting fallback to another configured provider.",
                        match.ProviderName);
                }
                else
                {
                    _logger.LogWarning(
                        "AI provider '{ProviderName}' was requested but not registered. Attempting fallback to another configured provider.",
                        candidate);
                }
            }

            var configuredFallback = providers.FirstOrDefault(p => p.IsConfigured);
            if (configuredFallback != null)
            {
                _logger.LogInformation("Using fallback AI provider '{ProviderName}'.", configuredFallback.ProviderName);
                return configuredFallback;
            }

            var registeredProviders = string.Join(", ", providers.Select(p => p.ProviderName));
            throw new InvalidOperationException($"No configured AI providers are available. Registered providers: {registeredProviders}.");
        }
    }
}
