using System.Diagnostics;
using System.Linq;
using System.Text;
using AiLogger.Api.Contracts;
using AiLogger.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
const string CorsPolicyName = "DevelopmentCors";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
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

app.MapPost("/api/logs/analyze", async Task<IResult> (
    [FromForm(Name = "file")] IFormFile? file,
    [FromForm(Name = "provider")] string? provider,
    [FromForm(Name = "notes")] string? notes,
    ILocalSensitiveDataDetector detector,
    TimeProvider timeProvider,
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
    logger.LogInformation("Analyzing log '{FileName}' (size: {FileLength} bytes, provider: {Provider}, notes?: {HasNotes})", file.FileName, file.Length, provider ?? "auto", !string.IsNullOrWhiteSpace(notes));

    var options = new SensitiveDataOptions();
    var localResult = detector.DetectAndReplace(content, options);

    measured.Stop();

    var findings = localResult.Mappings
        .GroupBy(m => (m.Type, m.Replacement))
        .Select(group => new SensitiveFindingDto(
            Category: group.Key.Type,
            ReplacementKey: group.Key.Replacement,
            Sample: group.First().Original,
            Occurrences: group.Count(),
            Confidence: 0.6
        ))
        .OrderByDescending(f => f.Occurrences)
        .ThenBy(f => f.Category)
        .ToArray();

    var normalizedProvider = string.IsNullOrWhiteSpace(provider) || string.Equals(provider, "auto", StringComparison.OrdinalIgnoreCase)
        ? "LocalDetector"
        : provider.Trim();

    var processedAt = timeProvider.GetUtcNow();
    var promptTokens = Math.Max(1, (int)Math.Ceiling(content.Length / 4.0));
    var completionTokens = Math.Max(0, findings.Length * 2);
    var totalTokens = promptTokens + completionTokens;

    var response = new LogAnalysisResponseDto(
        SanitizedContent: localResult.PrefilteredText,
        SensitiveFindings: findings,
        Metadata: new AnalysisMetadataDto(
            OriginalFileName: file.FileName,
            Provider: normalizedProvider,
            ProcessedAt: processedAt,
            DurationMs: measured.Elapsed.TotalMilliseconds,
            TokenUsage: new TokenUsageDto(promptTokens, completionTokens, totalTokens)
        ),
        Warnings: findings.Length == 0
            ? new[] { "No sensitive markers were detected. Review results before sharing externally." }
            : Array.Empty<string>()
    );

    return Results.Ok(response);
})
.DisableAntiforgery();

app.Run();
