using Xunit;
using Moq;
using AiLogger.Providers;
using AiLogger.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiLogger.Tests
{
    public class ProviderFactoryTests
    {
        [Fact]
        public void GetProvider_Returns_Default_Provider()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    {"AiProviders:Default", "TestProvider"}
                })
                .Build();

            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");
            mockProvider.Setup(p => p.IsConfigured).Returns(true);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IConfiguration>(config);
            serviceCollection.AddLogging();
            serviceCollection.AddScoped<IAIProvider>(sp => mockProvider.Object);
            serviceCollection.AddScoped<IAIProviderFactory, AIProviderFactory>();
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var factory = serviceProvider.GetRequiredService<IAIProviderFactory>();

            // Act
            var provider = factory.GetProvider();

            // Assert
            Assert.NotNull(provider);
            Assert.Equal("TestProvider", provider.ProviderName);
        }

        [Fact]
        public void GetProvider_Falls_Back_When_Default_Not_Configured()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    {"AiProviders:Default", "Unconfigured"}
                })
                .Build();

            var configuredProvider = new Mock<IAIProvider>();
            configuredProvider.Setup(p => p.ProviderName).Returns("Ollama");
            configuredProvider.Setup(p => p.IsConfigured).Returns(true);

            var unconfiguredProvider = new Mock<IAIProvider>();
            unconfiguredProvider.Setup(p => p.ProviderName).Returns("Unconfigured");
            unconfiguredProvider.Setup(p => p.IsConfigured).Returns(false);

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddScoped<IAIProvider>(sp => unconfiguredProvider.Object);
            services.AddScoped<IAIProvider>(sp => configuredProvider.Object);
            services.AddScoped<IAIProviderFactory, AIProviderFactory>();

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IAIProviderFactory>();

            // Act
            var resolved = factory.GetProvider();

            // Assert
            Assert.Equal("Ollama", resolved.ProviderName);
        }

        [Fact]
        public void GetProvider_Respects_Preferred_Provider_When_Configured()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    {"AiProviders:Default", "AzureOpenAI"}
                })
                .Build();

            var preferredProvider = new Mock<IAIProvider>();
            preferredProvider.Setup(p => p.ProviderName).Returns("Ollama");
            preferredProvider.Setup(p => p.IsConfigured).Returns(true);

            var defaultProvider = new Mock<IAIProvider>();
            defaultProvider.Setup(p => p.ProviderName).Returns("AzureOpenAI");
            defaultProvider.Setup(p => p.IsConfigured).Returns(false);

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            services.AddLogging();
            services.AddScoped<IAIProvider>(sp => defaultProvider.Object);
            services.AddScoped<IAIProvider>(sp => preferredProvider.Object);
            services.AddScoped<IAIProviderFactory, AIProviderFactory>();

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IAIProviderFactory>();

            // Act
            var resolved = factory.GetProvider("Ollama");

            // Assert
            Assert.Equal("Ollama", resolved.ProviderName);
        }
    }
}
