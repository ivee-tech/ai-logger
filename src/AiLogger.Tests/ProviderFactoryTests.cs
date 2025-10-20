using Xunit;
using Moq;
using AiLogger.Providers;
using AiLogger.Core;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace AiLogger.Tests
{
    public class ProviderFactoryTests
    {
        [Fact]
        public void GetProvider_Returns_Default_Provider()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> {
                    {"AiProviders:Default", "TestProvider"}
                })
                .Build();

            var mockProvider = new Mock<IAIProvider>();
            mockProvider.Setup(p => p.ProviderName).Returns("TestProvider");

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IConfiguration>(config);
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
    }
}
