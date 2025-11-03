using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AiLogger.Api.Contracts;
using AiLogger.Core;
using AiLogger.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
const string CorsPolicyName = "DevelopmentCors";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
        policy.AllowAnyHeader().AllowAnyMethod().WithOrigins(
            "http://localhost:4200",
            "http://127.0.0.1:4200"
        ));
});

builder.Services.AddSingleton<ILocalSensitiveDataDetector, RegexLocalSensitiveDataDetector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAIProvider, AzureOpenAIProvider>();
builder.Services.AddScoped<IAIProvider, OpenAIProvider>();
builder.Services.AddScoped<IAIProvider, OllamaProvider>();
builder.Services.AddScoped<IAIProviderFactory, AIProviderFactory>();
builder.Services.AddScoped<ILogSanitizationPipeline, LogSanitizationPipeline>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
}

if (app.Environment.IsDevelopment())
{
    app.UseCors(CorsPolicyName);
    app.MapOpenApi();
}

app.MapGet("/api/health", (TimeProvider timeProvider) =>
{
    var now = timeProvider.GetUtcNow();
    return Results.Ok(new
    {
        status = "Healthy",
        timestampUtc = now.UtcDateTime,
        uptimeSeconds = Environment.TickCount64 / 1000d
    });
}).WithName("Health");

app.MapPost("/api/logs/analyze", async Task<IResult> (
    [FromForm(Name = "file")] IFormFile? file,
    [FromForm(Name = "provider")] string? provider,
    [FromForm(Name = "notes")] string? notes,
    ILogSanitizationPipeline pipeline,
    TimeProvider timeProvider,
    IHostEnvironment hostEnvironment,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("A log file must be provided.");
    }

    if (file.Length > MaxFileSizeBytes)
    {
        var limitMb = MaxFileSizeBytes / (1024 * 1024);
        return Results.BadRequest($"Log files are limited to {limitMb} MB.");
    }

    var measured = Stopwatch.StartNew();

    string content;
    try
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        content = await reader.ReadToEndAsync(cancellationToken);
    }
    catch (IOException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: $"Failed to read the uploaded file: {ex.Message}");
    }

    var logger = loggerFactory.CreateLogger("LogAnalysis");

    var timestampSuffix = timeProvider.GetUtcNow().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
    var storageRoot = Path.Combine(hostEnvironment.ContentRootPath, "processed-logs");
    var safeStem = CreateSafeFileStem(file.FileName);
    var originalExtension = Path.GetExtension(file.FileName);

    string originalCopyPath;
    try
    {
        originalCopyPath = await SaveTimestampedFileAsync(storageRoot, safeStem, originalExtension, timestampSuffix, content, cancellationToken);
        logger.LogInformation("Saved timestamped input log copy to {Path}", originalCopyPath);
    }
    catch (IOException ex)
    {
        logger.LogError(ex, "Failed to persist uploaded log copy for '{FileName}'", file.FileName);
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: $"Failed to store the uploaded log: {ex.Message}");
    }

    logger.LogInformation("Analyzing log '{FileName}' (size: {FileLength} bytes, provider: {Provider}, notes?: {HasNotes})", file.FileName, file.Length, provider ?? "auto", !string.IsNullOrWhiteSpace(notes));

    var detectionOptions = new SensitiveDataOptions();
    var sanitizationOptions = new SanitizationOptions();

    var requestedProviderName = string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "auto", StringComparison.OrdinalIgnoreCase)
        ? null
        : provider.Trim();

    SanitizationPipelineResult pipelineResult;
    try
    {
        pipelineResult = await pipeline.SanitizeAsync(
            content,
            requestedProviderName,
            detectionOptions,
            sanitizationOptions,
            cancellationToken);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to sanitize log via pipeline for provider '{Provider}'.", requestedProviderName ?? "auto");
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: "Log sanitization failed. See server logs for details.");
    }

    logger.LogInformation(
        "Pipeline completed: Provider={Provider}, LocalReplacements={LocalCount}, UsedAI={UsedAi}",
        pipelineResult.ProviderName,
        pipelineResult.LocalReplacementCount,
        pipelineResult.UsedAiSuccessfully);

    if (!string.IsNullOrEmpty(pipelineResult.AnalysisJson))
    {
        logger.LogDebug("AI analysis payload length: {Length}", pipelineResult.AnalysisJson.Length);
    }

    if (!pipelineResult.UsedAiSuccessfully && !string.IsNullOrEmpty(pipelineResult.AiError))
    {
        logger.LogWarning(
            "AI provider fallback occurred. Attempted={Attempted}, Error={Error}",
            pipelineResult.AiProviderName ?? "auto",
            pipelineResult.AiError);
    }

    string sanitizedCopyPath;
    string sanitizedFileNameForDownload;
    try
    {
        var sanitizedStem = $"{safeStem}-sanitized";
        sanitizedCopyPath = await SaveTimestampedFileAsync(
            storageRoot,
            sanitizedStem,
            string.IsNullOrEmpty(originalExtension) ? ".log" : originalExtension,
            timestampSuffix,
            pipelineResult.SanitizedText,
            cancellationToken);
        logger.LogInformation("Saved timestamped sanitized log copy to {Path}", sanitizedCopyPath);
        sanitizedFileNameForDownload = Path.GetFileName(sanitizedCopyPath);
    }
    catch (IOException ex)
    {
        logger.LogError(ex, "Failed to persist sanitized log copy for '{FileName}'", file.FileName);
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError, detail: $"Failed to store the sanitized log: {ex.Message}");
    }

    measured.Stop();

    var findings = pipelineResult.Mappings
        .GroupBy(m => (m.Type, m.Replacement))
        .Select(group => new SensitiveFindingDto(
            Category: group.Key.Type,
            ReplacementKey: group.Key.Replacement,
            Sample: group.First().Original,
            Occurrences: group.Count(),
            Confidence: 0.6))
        .OrderByDescending(f => f.Occurrences)
        .ThenBy(f => f.Category)
        .ToArray();

    var warnings = new List<string>();
    if (findings.Length == 0)
    {
        warnings.Add("No sensitive markers were detected. Review results before sharing externally.");
    }
    if (!pipelineResult.UsedAiSuccessfully && !string.IsNullOrEmpty(pipelineResult.AiError))
    {
        warnings.Add($"AI provider fallback: {pipelineResult.AiError}");
    }

    var processedAt = timeProvider.GetUtcNow();
    var promptTokens = Math.Max(1, (int)Math.Ceiling(content.Length / 4.0));
    var completionTokens = Math.Max(0, findings.Length * 2);
    var totalTokens = promptTokens + completionTokens;

    var response = new LogAnalysisResponseDto(
        SanitizedContent: pipelineResult.SanitizedText,
        SensitiveFindings: findings,
        Metadata: new AnalysisMetadataDto(
            OriginalFileName: file.FileName,
            Provider: pipelineResult.ProviderName,
            ProcessedAt: processedAt,
            DurationMs: measured.Elapsed.TotalMilliseconds,
            TokenUsage: new TokenUsageDto(promptTokens, completionTokens, totalTokens),
            SanitizedFileName: sanitizedFileNameForDownload),
        Warnings: warnings.ToArray());

    logger.LogInformation(
        "Returning sanitized response for {FileName} using provider {Provider}.",
        file.FileName,
        pipelineResult.ProviderName);

    return Results.Ok(response);
})
.DisableAntiforgery();

app.Run();

static async Task<string> SaveTimestampedFileAsync(
    string directory,
    string stem,
    string extension,
    string timestampSuffix,
    string content,
    CancellationToken cancellationToken)
{
    Directory.CreateDirectory(directory);

    var safeStem = string.IsNullOrWhiteSpace(stem) ? "uploaded-log" : stem;
    if (safeStem.Length > 64)
    {
        safeStem = safeStem[..64];
    }
    var normalizedExtension = NormalizeExtension(extension);

    string candidate = BuildTimestampedPath(directory, safeStem, normalizedExtension, timestampSuffix);
    int counter = 1;
    while (File.Exists(candidate))
    {
        candidate = BuildTimestampedPath(directory, safeStem, normalizedExtension, $"{timestampSuffix}_{counter++:D2}");
    }

    await File.WriteAllTextAsync(candidate, content, cancellationToken);
    return candidate;
}

static string BuildTimestampedPath(string directory, string stem, string extension, string suffix)
{
    var fileName = string.IsNullOrEmpty(extension)
        ? $"{stem}.{suffix}"
        : $"{stem}.{suffix}{extension}";
    return Path.Combine(directory, fileName);
}

static string NormalizeExtension(string extension)
{
    if (string.IsNullOrWhiteSpace(extension))
    {
        return string.Empty;
    }

    return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
}

static string CreateSafeFileStem(string fileName)
{
    var stem = Path.GetFileNameWithoutExtension(fileName);
    if (string.IsNullOrWhiteSpace(stem))
    {
        stem = "uploaded-log";
    }

    var invalidChars = Path.GetInvalidFileNameChars();
    var sb = new StringBuilder(stem.Length);
    foreach (var ch in stem)
    {
        sb.Append(invalidChars.Contains(ch) ? '_' : ch);
    }

    var safe = sb.ToString();
    if (safe.Length > 64)
    {
        safe = safe[..64];
    }

    return string.IsNullOrWhiteSpace(safe) ? "uploaded-log" : safe;
}

