using AiLogger.Core;
using AiLogger.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AiLogger.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("AI Logger started.");

            var processor = host.Services.GetRequiredService<ILogProcessor>();
            await processor.ProcessAsync();

            logger.LogInformation("AI Logger finished.");
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configuration) =>
                {
                    // Keep default providers (which set the correct base path) then add our JSON/environment/CLI layers.
                    var env = hostingContext.HostingEnvironment;
                    configuration.SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables();
                    if (args is { Length: > 0 })
                    {
                        configuration.AddCommandLine(args);
                    }
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();
                    services.AddScoped<IAIProvider, AzureOpenAIProvider>();
                    services.AddScoped<IAIProvider, OpenAIProvider>();
                    services.AddScoped<IAIProvider, OllamaProvider>();
                    services.AddScoped<IAIProviderFactory, AIProviderFactory>();
                    services.AddSingleton<ILocalSensitiveDataDetector, RegexLocalSensitiveDataDetector>();
                    services.AddScoped<ILogProcessor, LogProcessor>();
                });
    }

    public class LogProcessor : ILogProcessor
    {
        private readonly ILogger<LogProcessor> _logger;
        private readonly IAIProvider _aiProvider;
        private readonly ILocalSensitiveDataDetector _localDetector;

        public LogProcessor(ILogger<LogProcessor> logger, IAIProviderFactory aiProviderFactory, ILocalSensitiveDataDetector localDetector)
        {
            _logger = logger;
            _aiProvider = aiProviderFactory.GetProvider();
            _localDetector = localDetector;
        }

        public async Task ProcessAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"Using AI provider: {_aiProvider.ProviderName}");
            _logger.LogInformation("Processing logs...");

            var logFilePath = Path.Combine(AppContext.BaseDirectory, "system_logs");
            if (!File.Exists(logFilePath))
            {
                _logger.LogWarning("Log file not found at {Path}, creating sample.", logFilePath);
                await File.WriteAllTextAsync(logFilePath, "2025-10-04 10:00:00 INFO Sample log line", cancellationToken);
            }
            string originalContent = await File.ReadAllTextAsync(logFilePath, cancellationToken);

            // 0. Local regex pre-filter (token + privacy optimization)
            var localOptions = new SensitiveDataOptions();
            var localResult = _localDetector.DetectAndReplace(originalContent, localOptions);
            var prefilteredContent = localResult.PrefilteredText;
            if (localResult.Mappings.Count > 0)
            {
                _logger.LogInformation("Local pre-filter replaced {Count} items before AI call.", localResult.Mappings.Count);
            }

            // 1. Analysis phase (runs on prefiltered content to avoid sending raw secrets)
            var analysisJson = await _aiProvider.AnalyzeTextAsync(prefilteredContent, localOptions, cancellationToken);
            _logger.LogInformation("Analysis complete. Raw model output length={Length}", analysisJson?.Length ?? 0);
            _logger.LogDebug("Analysis JSON: {Analysis}", analysisJson);

            // 2. Sanitization phase
            _logger.LogInformation("Starting sanitization phase...");
            var sanitizeOptions = new SanitizationOptions();
            var aiResult = await _aiProvider.SanitizeAsync(prefilteredContent, sanitizeOptions, cancellationToken);

            SanitizationResult finalResult;
            if (!aiResult.Success)
            {
                _logger.LogWarning("AI sanitization failed or skipped; falling back to local-only sanitization: {Error}", aiResult.Error ?? "Unknown");
                finalResult = new SanitizationResult
                {
                    OriginalText = originalContent,
                    SanitizedText = localResult.PrefilteredText,
                    Mappings = localResult.Mappings,
                    Success = true,
                    Error = aiResult.Error
                };
            }
            else
            {
                var sanitizedText = aiResult.SanitizedText;
                var filteredMappings = new List<MappingEntry>();
                foreach (var mapping in aiResult.Mappings)
                {
                    if (IsFalsePositiveHostname(mapping))
                    {
                        if (!string.IsNullOrEmpty(mapping.Replacement) && sanitizedText.Contains(mapping.Replacement, StringComparison.Ordinal))
                        {
                            sanitizedText = sanitizedText.Replace(mapping.Replacement, mapping.Original, StringComparison.Ordinal);
                        }
                        continue;
                    }
                    filteredMappings.Add(mapping);
                }

                // Merge mappings (local first, then AI avoiding duplicate originals)
                var merged = new List<MappingEntry>(localResult.Mappings);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in localResult.Mappings) seen.Add(m.Original);
                foreach (var m in filteredMappings)
                {
                    if (!seen.Contains(m.Original))
                    {
                        merged.Add(m);
                        seen.Add(m.Original);
                    }
                }
                finalResult = new SanitizationResult
                {
                    OriginalText = originalContent,
                    SanitizedText = sanitizedText,
                    Mappings = merged,
                    Success = true
                };
            }
            var result = finalResult;

            // 3. Persist sanitized output & mapping
            var sanitizedPath = Path.ChangeExtension(logFilePath, ".sanitized.log");
            await File.WriteAllTextAsync(sanitizedPath, result.SanitizedText, cancellationToken);

            var mappingPath = Path.ChangeExtension(logFilePath, ".mappings.json");
            var mappingDoc = new
            {
                sourceFile = Path.GetFileName(logFilePath),
                sanitizedFile = Path.GetFileName(sanitizedPath),
                generatedUtc = DateTime.UtcNow,
                provider = _aiProvider.ProviderName,
                mappingCount = result.Mappings.Count,
                mappings = result.Mappings
            };
            var mappingJson = JsonSerializer.Serialize(mappingDoc, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(mappingPath, mappingJson, cancellationToken);

            _logger.LogInformation("Sanitization complete. Wrote sanitized file: {Sanitized} and mapping file: {Mapping}", sanitizedPath, mappingPath);
            if (result.Mappings.Count == 0)
            {
                _logger.LogInformation("No sensitive items were replaced.");
            }
            else
            {
                _logger.LogInformation("Replaced {Count} sensitive items.", result.Mappings.Count);
            }
        }

        private static bool IsFalsePositiveHostname(MappingEntry mapping)
        {
            if (!string.Equals(mapping.Type, "Hostname", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = mapping.Original?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (value.Contains(' '))
            {
                return true;
            }

            if (value.Contains(':'))
            {
                var segments = value.Split(':');
                if (segments.Length >= 2 && segments.All(s => s.All(char.IsDigit)))
                {
                    return true; // looks like HH:mm[:ss] timestamp
                }
            }

            return false;
        }
    }
}
