using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiLogger.Core;
using AiLogger.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiLogger.Console;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        using var host = CreateHostBuilder(args).Build();
        using var scope = host.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<ILogProcessor>();
        await processor.ProcessAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, configuration) =>
            {
                var environment = context.HostingEnvironment;
                configuration.SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables();
                if (args is { Length: > 0 })
                {
                    configuration.AddCommandLine(args);
                }
            })
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();

                services.AddScoped<IAIProvider, AzureOpenAIProvider>();
                services.AddScoped<IAIProvider, OpenAIProvider>();
                services.AddScoped<IAIProvider, OllamaProvider>();
                services.AddScoped<IAIProviderFactory, AIProviderFactory>();
                services.AddSingleton<ILocalSensitiveDataDetector, RegexLocalSensitiveDataDetector>();
                services.AddScoped<ILogSanitizationPipeline, LogSanitizationPipeline>();

                services.AddOptions<SanitizationOptions>().Bind(context.Configuration.GetSection("Sanitization"));
                services.AddOptions<SensitiveDataOptions>().Bind(context.Configuration.GetSection("Detection"));

                services.AddScoped<ILogProcessor, LogProcessor>();
            });
}

internal sealed class LogProcessor : ILogProcessor
{
    private readonly ILogger<LogProcessor> _logger;
    private readonly ILogSanitizationPipeline _pipeline;
    private readonly IOptions<SensitiveDataOptions> _detectionOptions;
    private readonly IOptions<SanitizationOptions> _sanitizationOptions;

    public LogProcessor(
        ILogger<LogProcessor> logger,
        ILogSanitizationPipeline pipeline,
        IOptions<SensitiveDataOptions> detectionOptions,
        IOptions<SanitizationOptions> sanitizationOptions)
    {
        _logger = logger;
        _pipeline = pipeline;
        _detectionOptions = detectionOptions;
        _sanitizationOptions = sanitizationOptions;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing logs...");

        var logFilePath = Path.Combine(AppContext.BaseDirectory, "system_logs");
        if (!File.Exists(logFilePath))
        {
            const string sampleContent = "2025-10-04 10:00:00 INFO Sample log line";
            await File.WriteAllTextAsync(logFilePath, sampleContent, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created sample log file at {Path}", logFilePath);
        }

        var originalContent = await File.ReadAllTextAsync(logFilePath, cancellationToken).ConfigureAwait(false);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

        var inputBackupPath = CreateTimestampedCopy(logFilePath, originalContent, timestamp);
        _logger.LogInformation("Created timestamped copy of input log: {Path}", inputBackupPath);

        var pipelineResult = await _pipeline.SanitizeAsync(
            originalContent,
            preferredProvider: null,
            _detectionOptions.Value ?? new SensitiveDataOptions(),
            _sanitizationOptions.Value ?? new SanitizationOptions(),
            cancellationToken).ConfigureAwait(false);

        if (!pipelineResult.UsedAiSuccessfully && !string.IsNullOrEmpty(pipelineResult.AiError))
        {
            _logger.LogWarning("AI provider fallback: {Message}", pipelineResult.AiError);
        }

        if (!string.IsNullOrEmpty(pipelineResult.AnalysisJson))
        {
            _logger.LogDebug("AI analysis response length: {Length}", pipelineResult.AnalysisJson.Length);
        }

        _logger.LogInformation("Provider used for sanitization: {Provider}", pipelineResult.ProviderName);

        var sanitizedPath = Path.ChangeExtension(logFilePath, ".sanitized.log") ?? (logFilePath + ".sanitized.log");
        await File.WriteAllTextAsync(sanitizedPath, pipelineResult.SanitizedText, cancellationToken).ConfigureAwait(false);
        var sanitizedBackupPath = CreateTimestampedCopy(sanitizedPath, pipelineResult.SanitizedText, timestamp);
        _logger.LogInformation("Created timestamped copy of sanitized log: {Path}", sanitizedBackupPath);

        var mappingPath = Path.ChangeExtension(logFilePath, ".mappings.json") ?? (logFilePath + ".mappings.json");
        var mappingDoc = new
        {
            sourceFile = Path.GetFileName(logFilePath),
            sanitizedFile = Path.GetFileName(sanitizedPath),
            generatedUtc = DateTime.UtcNow,
            provider = pipelineResult.ProviderName,
            usedAi = pipelineResult.UsedAiSuccessfully,
            aiProviderAttempted = pipelineResult.AiProviderName,
            aiError = pipelineResult.AiError,
            mappingCount = pipelineResult.Mappings.Count,
            mappings = pipelineResult.Mappings
        };
        var mappingJson = JsonSerializer.Serialize(mappingDoc, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(mappingPath, mappingJson, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Sanitization complete. Writing sanitized file {Sanitized} and mapping file {Mapping}.",
            sanitizedPath,
            mappingPath);

        if (pipelineResult.Mappings.Count == 0)
        {
            _logger.LogInformation("No sensitive items were replaced.");
        }
        else
        {
            _logger.LogInformation("Replaced {Count} sensitive items.", pipelineResult.Mappings.Count);
        }
    }

    private static string CreateTimestampedCopy(string originalPath, string content, string timestamp)
    {
        var directory = Path.GetDirectoryName(originalPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = AppContext.BaseDirectory;
        }

        var baseName = Path.GetFileNameWithoutExtension(originalPath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = Path.GetFileName(originalPath) ?? "log";
        }

        var extension = Path.GetExtension(originalPath);
        var candidate = BuildTimestampedPath(directory, baseName, extension, timestamp);
        var counter = 1;

        while (File.Exists(candidate))
        {
            candidate = BuildTimestampedPath(directory, baseName, extension, $"{timestamp}_{counter++:D2}");
        }

        File.WriteAllText(candidate, content);
        return candidate;
    }

    private static string BuildTimestampedPath(string directory, string baseName, string extension, string suffix)
    {
        var fileName = string.IsNullOrEmpty(extension)
            ? $"{baseName}.{suffix}"
            : $"{baseName}.{suffix}{extension}";
        return Path.Combine(directory, fileName);
    }
}
