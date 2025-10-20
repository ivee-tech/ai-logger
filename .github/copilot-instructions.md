# GitHub Copilot Instructions for AI Log Sanitization Tool

## Project Overview
This is a .NET console application that uses AI to identify and replace sensitive information in log files with mock data while maintaining a secure mapping between original and sanitized values.

## Core Functionality
- **Log Processing**: Parse various log formats (JSON, XML, plain text, structured logs)
- **AI-Powered Detection**: Use AI models to identify sensitive information patterns
- **Data Sanitization**: Replace sensitive data with consistent mock values
- **Secure Mapping**: Maintain encrypted mappings between original and mock data
- **Multi-Provider Support**: Abstract interface supporting Azure AI Foundry, OpenAI, GitHub Models, and Ollama

## Technology Stack & Preferences

### Primary Technologies
- **.NET 8+**: Target framework for cross-platform compatibility
- **C# 12**: Use latest language features including primary constructors, collection expressions
- **Microsoft.Extensions.Hosting**: Dependency injection and configuration
- **Microsoft.Identity.Web**: Entra ID authentication
- **Azure.AI.OpenAI**: Azure AI services integration
- **System.Text.Json**: JSON processing (prefer over Newtonsoft.Json)

### Key Dependencies
```xml
<PackageReference Include="Microsoft.Extensions.Hosting" />
<PackageReference Include="Microsoft.Identity.Web" />
<PackageReference Include="Azure.AI.OpenAI" />
<PackageReference Include="Azure.Security.KeyVault.Secrets" />
<PackageReference Include="OpenAI" />
<PackageReference Include="Microsoft.Extensions.Logging" />
<PackageReference Include="Microsoft.Extensions.Configuration" />
```

## Architecture Patterns

### Dependency Injection
- Use constructor injection for all dependencies
- Register services in `Program.cs` using `IServiceCollection`
- Prefer interface-based abstractions for testability

### Configuration Management
- Use `IOptions<T>` pattern for strongly-typed configuration
- Store sensitive values in Azure Key Vault
- Support environment variable overrides
- Use `appsettings.json` for non-sensitive defaults

### Async/Await Patterns
- Use async/await for all I/O operations
- Prefer `ConfigureAwait(false)` in library code
- Use `CancellationToken` for long-running operations
- Implement proper error handling with try-catch blocks

## Code Style Guidelines

### Naming Conventions
- Use PascalCase for public members, classes, and methods
- Use camelCase for private fields and local variables
- Prefix interfaces with 'I' (e.g., `IAIProvider`)
- Use descriptive names that indicate purpose

### File Organization
```
src/
├── AiLogger.Core/           # Core business logic
├── AiLogger.Providers/      # AI provider implementations
├── AiLogger.Security/       # Security and encryption utilities
├── AiLogger.Storage/        # Data persistence layer
├── AiLogger.Console/        # CLI application
└── AiLogger.Tests/          # Unit and integration tests
```

### Error Handling
- Use custom exceptions that inherit from appropriate base classes
- Include detailed error messages and context
- Log errors with structured logging
- Implement retry logic for transient failures

## AI Provider Interface Pattern

### Core Interface
```csharp
public interface IAIProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<SensitiveDataAnalysis> AnalyzeAsync(string content, CancellationToken cancellationToken);
    Task<SanitizationResult> SanitizeAsync(string content, SanitizationOptions options, CancellationToken cancellationToken);
}
```

### Implementation Guidelines
- Each provider should validate configuration on startup
- Implement proper rate limiting and retry logic
- Use structured logging for debugging and monitoring
- Handle provider-specific errors gracefully

## Security Best Practices

### Authentication & Authorization
- Use Managed Identity when running in Azure
- Implement proper token caching and refresh
- Validate all inputs and sanitize outputs
- Use RBAC for access control

### Data Protection
- Encrypt sensitive data at rest using Azure Key Vault
- Use SecureString for in-memory sensitive data
- Implement secure deletion of temporary files
- Never log sensitive information

### Secure Coding
```csharp
// Example: Secure configuration access
public class SecureConfigurationService
{
    private readonly KeyVaultClient _keyVaultClient;
    
    public async Task<string> GetSecretAsync(string secretName)
    {
        // Always use try-catch for external calls
        // Implement proper logging without exposing secrets
        // Use cancellation tokens
    }
}
```

## Sensitive Data Categories

### Detection Patterns
- **IP Addresses**: IPv4/IPv6 patterns with context validation
- **Hostnames**: DNS-valid hostnames and FQDNs
- **User Identifiers**: Email addresses, usernames, employee IDs
- **System Resources**: Database names, server names, application names
- **Credentials**: API keys, tokens, passwords (pattern-based detection)
- **Custom Patterns**: Configurable regex patterns for organization-specific data

### Mock Data Generation
- Generate contextually appropriate replacements
- Maintain consistency within processing session
- Use deterministic algorithms for reproducible results
- Preserve data format and structure

## Container & Deployment Considerations

### Docker Best Practices
- Use multi-stage builds for smaller images
- Run as non-root user (UID 1001)
- Include health checks
- Minimize layer count and image size

### Kubernetes Integration
- Support configuration via ConfigMaps and Secrets
- Implement proper resource limits and requests
- Use readiness and liveness probes
- Support horizontal pod autoscaling

## Testing Strategy

### Unit Testing
- Use xUnit for test framework
- Mock external dependencies using Moq
- Test both success and failure scenarios
- Aim for >80% code coverage

### Integration Testing
- Test AI provider integrations with real services
- Validate end-to-end processing workflows
- Test container deployment scenarios
- Verify security controls and access restrictions

## Logging & Monitoring

### Structured Logging
```csharp
_logger.LogInformation("Processing file {FileName} with {RecordCount} records", 
    fileName, recordCount);
```

### Performance Metrics
- Track processing times and throughput
- Monitor AI provider response times
- Measure memory and CPU usage
- Count successful vs failed operations

## Configuration Examples

### appsettings.json Structure
```json
{
  "AiProviders": {
    "Default": "AzureOpenAI",
    "AzureOpenAI": {
      "Endpoint": "https://your-resource.openai.azure.com/",
      "Model": "gpt-4"
    }
  },
  "Processing": {
    "BatchSize": 100,
    "MaxParallelism": 4,
    "TimeoutSeconds": 30
  }
}
```

## Common Code Patterns

### Service Registration
```csharp
services.AddScoped<IAIProvider, AzureOpenAIProvider>();
services.AddScoped<ILogProcessor, LogProcessor>();
services.Configure<AiProviderOptions>(configuration.GetSection("AiProviders"));
```

### Async Processing with Cancellation
```csharp
public async Task ProcessAsync(CancellationToken cancellationToken = default)
{
    using var activity = ActivitySource.StartActivity("ProcessLog");
    try
    {
        // Implementation with proper cancellation support
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("Operation was cancelled");
        throw;
    }
}
```

## Performance Considerations
- Use `Memory<T>` and `Span<T>` for efficient memory usage
- Implement connection pooling for HTTP clients
- Use async enumerable for streaming large files
- Cache frequently accessed data appropriately

## Cross-Platform Compatibility
- Use `Path.Combine()` instead of hardcoded path separators
- Abstract platform-specific operations behind interfaces
- Test on both Windows and Linux environments
- Use environment variables for platform-specific configuration

## AI Model Integration Guidelines
- Prefer GitHub Models for development (cost-effective)
- Use Azure AI Foundry for production (enterprise features)
- Implement graceful fallback between providers
- Monitor token usage and implement cost controls
- Support both cloud and local model deployment

This file should help GitHub Copilot provide more contextually relevant suggestions throughout the development process.